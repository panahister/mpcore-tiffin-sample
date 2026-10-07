using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Tiffin.Media.Application.Ports;

namespace Tiffin.Media.Infrastructure.Store;

/// <summary>Where the store is, and who this service is there.</summary>
public sealed class S3Options
{
    /// <summary>The address of the store's S3 API.</summary>
    public required Uri Endpoint { get; init; }

    public required string Bucket { get; init; }

    public required string AccessKey { get; init; }

    /// <summary>From user secrets or the environment; never in a committed file.</summary>
    public required string SecretKey { get; init; }

    /// <summary>A store that is not Amazon's has no regions and is told any; the signature names one.</summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>Whether the bucket is made when it is not there. A developer's machine says yes; elsewhere it is made on purpose.</summary>
    public bool CreateBucket { get; init; }

    /// <summary>Exact browser origins that may use a signed upload address. Empty means infrastructure owns CORS.</summary>
    public IReadOnlyCollection<string> BrowserOrigins { get; init; } = [];

    /// <summary>What a log may show of these settings: never a key.</summary>
    public override string ToString() => $"{nameof(S3Options)} {{ Endpoint = {Endpoint}, Bucket = {Bucket} }}";
}

/// <summary>
/// The adapter for a store that speaks the S3 API. Which store that is, is a setting: the sample runs
/// RustFS and SeaweedFS behind it, and this file is the same for both.
/// </summary>
/// <remarks>
/// <para>
/// <b>Addressed by path.</b> Amazon addresses a bucket as a host name (<c>bucket.s3.amazonaws.com</c>);
/// a store on one's own network is addressed by path (<c>host/bucket/key</c>), which needs no entry in
/// the name service per bucket.
/// </para>
/// <para>
/// <b>An address that is signed</b> (AWS Signature Version 4) is good for one key, one verb and, for an
/// upload, one type, until it expires. Whoever holds it needs no key of the store and learns none.
/// </para>
/// <para>
/// The client is built here and kept for the life of the host: it is safe to share, and Wolverine, which
/// writes the code that builds a handler's dependencies, reads a registration by type and refuses one that
/// is a lambda.
/// </para>
/// </remarks>
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private readonly AmazonS3Client client;
    private readonly S3Options options;
    private readonly ILogger<S3ObjectStore> logger;
    private readonly SemaphoreSlim once = new(1, 1);
    private bool uploadBucketIsReady;

    public S3ObjectStore(S3Options options, ILogger<S3ObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.logger = logger;
        client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.Endpoint.AbsoluteUri,
                ForcePathStyle = true,
                AuthenticationRegion = options.Region,
                // A checksum in a trailer is Amazon's addition of 2025; a store that speaks the S3 API of
                // before is sent one only where the API requires it.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                Timeout = TimeSpan.FromSeconds(5),
                MaxErrorRetry = 2
            });
    }

    public async Task<Uri> SignUploadAsync(string key, string contentType, TimeSpan validFor, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(cancellationToken).ConfigureAwait(false);
        return await SignAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket, Key = key, Verb = HttpVerb.PUT, ContentType = contentType, Expires = DateTime.UtcNow.Add(validFor),
            Protocol = ProtocolOf(options.Endpoint)
        }).ConfigureAwait(false);
    }

    public Task<Uri> SignDownloadAsync(string key, TimeSpan validFor, CancellationToken cancellationToken) =>
        SignAsync(new GetPreSignedUrlRequest
        {
            BucketName = options.Bucket, Key = key, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.Add(validFor),
            Protocol = ProtocolOf(options.Endpoint)
        });

    public async Task<StoredObject?> LookAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var found = await client.GetObjectMetadataAsync(options.Bucket, key, cancellationToken).ConfigureAwait(false);
            return new StoredObject(found.ContentLength, found.Headers.ContentType);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception exception) when (IsTheStoresFault(exception))
        {
            throw Unavailable("look at", exception);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteObjectAsync(options.Bucket, key, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Nothing was there: that is what was asked for.
        }
        catch (Exception exception) when (IsTheStoresFault(exception))
        {
            throw Unavailable("remove", exception);
        }
    }

    public void Dispose()
    {
        client.Dispose();
        once.Dispose();
    }

    private async Task<Uri> SignAsync(GetPreSignedUrlRequest request)
    {
        try
        {
            // Signing is arithmetic: the store is not asked.
            return new Uri(await client.GetPreSignedURLAsync(request).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsTheStoresFault(exception))
        {
            throw Unavailable("sign an address for", exception);
        }
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (uploadBucketIsReady || (!options.CreateBucket && options.BrowserOrigins.Count == 0))
        {
            return;
        }

        await once.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (uploadBucketIsReady)
            {
                return;
            }

            if (options.CreateBucket)
            {
                try
                {
                    await client.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket }, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Made the bucket {Bucket}", options.Bucket);
                }
                catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                {
                    // Another instance made it a moment ago, or it was there all along.
                }
            }

            if (options.BrowserOrigins.Count > 0)
            {
                await client.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
                {
                    BucketName = options.Bucket,
                    Configuration = BuildBrowserUploadCors(options.BrowserOrigins)
                }, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Configured browser upload CORS for {OriginCount} exact origin(s)", options.BrowserOrigins.Count);
            }

            uploadBucketIsReady = true;
        }
        catch (Exception exception) when (IsTheStoresFault(exception))
        {
            throw Unavailable("make the bucket in", exception);
        }
        finally
        {
            once.Release();
        }
    }

    internal static CORSConfiguration BuildBrowserUploadCors(IEnumerable<string> configuredOrigins)
    {
        ArgumentNullException.ThrowIfNull(configuredOrigins);
        var origins = configuredOrigins.Select(NormalizeOrigin).Distinct(StringComparer.Ordinal).ToList();
        if (origins.Count == 0)
        {
            throw new ArgumentException("At least one browser origin is required.", nameof(configuredOrigins));
        }

        return new CORSConfiguration
        {
            Rules =
            [
                new CORSRule
                {
                    Id = "browser-signed-upload",
                    AllowedOrigins = origins,
                    AllowedMethods = ["PUT"],
                    AllowedHeaders = ["content-type"],
                    MaxAgeSeconds = 600
                }
            ]
        };
    }

    private static string NormalizeOrigin(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured) || !Uri.TryCreate(configured, UriKind.Absolute, out var origin)
            || (!origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new ArgumentException("A browser upload origin must be an exact HTTP or HTTPS origin.", nameof(configured));
        }

        return origin.GetLeftPart(UriPartial.Authority);
    }

    private static Protocol ProtocolOf(Uri endpoint) => endpoint.Scheme == Uri.UriSchemeHttps ? Protocol.HTTPS : Protocol.HTTP;

    private static bool IsTheStoresFault(Exception exception) =>
        exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false };

    private StoreUnavailableException Unavailable(string what, Exception exception)
    {
        // Never an address: a signed one is a credential.
        logger.LogWarning(exception, "Could not {What} the store", what);
        return new StoreUnavailableException($"Could not {what} the store.", exception);
    }
}
