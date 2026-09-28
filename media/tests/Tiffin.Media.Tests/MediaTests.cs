using MPCore.Application.Results;
using Tiffin.Media.Application.Commands;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Application.Queries;
using Tiffin.Media.Application.Validators;
using Tiffin.Media.Application.Views;
using Tiffin.Media.Domain;
using Tiffin.Media.Domain.Events;
using Tiffin.Media.Tests.Support;

namespace Tiffin.Media.Tests;

public sealed class FakeFiles : IMediaRepository, IMediaReadModel
{
    private readonly List<MediaFile> items = [];

    public IReadOnlyList<MediaFile> All => items;

    public Task<MediaFile?> GetAsync(Guid id, string city, CancellationToken cancellationToken) => Task.FromResult(items.Find(f => f.Id == id && f.City == city));

    public Task<MediaFile?> FindAsync(Guid id, string city, CancellationToken cancellationToken) => GetAsync(id, city, cancellationToken);

    public void Add(MediaFile file) => items.Add(file);
}

/// <summary>The store: holds what a test puts into it, signs addresses that name their key, and may be down.</summary>
public sealed class FakeStore : IObjectStore
{
    public Dictionary<string, StoredObject> Objects { get; } = [];

    public bool IsDown { get; set; }

    public Task<Uri> SignUploadAsync(string key, string contentType, TimeSpan validFor, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(new Uri($"https://store.invalid/bucket/{key}?X-Amz-Signature=secret-for-upload")));

    public Task<Uri> SignDownloadAsync(string key, TimeSpan validFor, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(new Uri($"https://store.invalid/bucket/{key}?X-Amz-Signature=secret-for-download")));

    public Task<StoredObject?> LookAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Answer(Objects.GetValueOrDefault(key)));

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Answer(0);
        Objects.Remove(key);
        return Task.CompletedTask;
    }

    private T Answer<T>(T value) => IsDown ? throw new StoreUnavailableException("The store did not answer.") : value;
}

/// <summary>A file: announced here, sent to the store, and available when what arrived is what was announced.</summary>
public sealed class MediaTests
{
    private readonly FakeFiles files = new();
    private readonly FakeStore store = new();
    private readonly FakeAudit audit = new();
    private readonly FakeClock clock = FakeClock.At2026();
    private readonly FakeActor mina = FakeActor.User("mina", "restaurant-manager");

    private Task<Result<UploadTicket>> Announce(string purpose = "restaurant-picture", string type = "image/png", long size = 70, FakeTenant? tenant = null) =>
        MediaCommandsHandler.Handle(
            new ReserveUpload(purpose, "front.png", type, size), mina, tenant ?? FakeTenant.Tehran(), files, store, new FakeUnitOfWork(), clock, default);

    private Task<Result<MediaView>> Confirm(Guid id, FakeActor? by = null, FakeTenant? tenant = null) => MediaCommandsHandler.Handle(
        new ConfirmUpload(id), by ?? mina, tenant ?? FakeTenant.Tehran(), files, store, audit, new FakeUnitOfWork(), clock, default);

    private Task<Result<MediaView>> Delete(Guid id, FakeActor? by = null) => MediaCommandsHandler.Handle(
        new DeleteMedia(id), by ?? mina, FakeTenant.Tehran(), files, store, audit, new FakeUnitOfWork(), clock, default);

    private async Task<MediaFile> Available()
    {
        var ticket = (await Announce()).Value;
        var file = files.All.Single(f => f.Id == ticket.MediaId);
        store.Objects[file.StorageKey] = new StoredObject(70, "image/png");
        await Confirm(file.Id);
        file.ClearEvents();
        return file;
    }

    [Fact]
    public async Task A_place_is_reserved_under_a_key_of_the_city_that_no_request_can_name()
    {
        var ticket = (await Announce()).Value;

        var file = Assert.Single(files.All);
        Assert.Equal($"tehran/restaurant-picture/{file.Id:N}", file.StorageKey);
        Assert.Equal((MediaState.Pending, "mina", clock.UtcNow + MediaFile.UploadWindow), (file.State, file.OwnerId, file.ExpiresOnUtc));
        Assert.Contains(file.StorageKey, ticket.UploadUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("PUT", ticket.Method);
    }

    [Fact]
    public async Task An_address_is_a_credential_and_is_not_printed()
    {
        var ticket = (await Announce()).Value;

        Assert.DoesNotContain("secret", ticket.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("store.invalid", ticket.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("holiday-video", "video/mp4", 2000, "PURPOSE_UNKNOWN")]
    [InlineData("restaurant-picture", "application/pdf", 2000, "TYPE_NOT_ALLOWED")]
    [InlineData("restaurant-picture", "image/png", 0, "SIZE_NOT_ALLOWED")]
    [InlineData("restaurant-picture", "image/png", 5 * 1024 * 1024 + 1, "SIZE_NOT_ALLOWED")]
    public async Task What_a_file_may_be_is_decided_by_what_it_is_for(string purpose, string type, long size, string rule)
    {
        Assert.Equal(rule, await Rules.BrokenAsync(() => Announce(purpose, type, size)));
        Assert.Empty(files.All);
    }

    [Fact]
    public async Task A_document_of_a_courier_may_be_a_pdf_of_ten_megabytes_and_a_picture_may_not()
    {
        Assert.True((await Announce("courier-document", "application/pdf", 10 * 1024 * 1024)).IsSuccess);
        Assert.Equal("SIZE_NOT_ALLOWED", await Rules.BrokenAsync(() => Announce("menu-picture", "image/webp", 10 * 1024 * 1024)));
    }

    [Fact]
    public async Task A_file_is_available_when_what_arrived_is_what_was_announced_and_says_so_on_the_stream()
    {
        var ticket = (await Announce()).Value;
        var file = Assert.Single(files.All);

        Assert.Equal("NOTHING_ARRIVED", (await Confirm(ticket.MediaId)).FailureDescriptor!.Identity.Code);
        store.Objects[file.StorageKey] = new StoredObject(70, "IMAGE/PNG");
        var view = (await Confirm(ticket.MediaId)).Value;

        Assert.Equal("Available", view.State);
        var available = Assert.IsType<FileAvailable>(Assert.Single(file.IntegrationEvents));
        Assert.Equal((file.Id, "tehran", "restaurant-picture", 70L), (available.MediaId, available.City, available.Purpose, available.Size));
        Assert.Equal("FILE_NOT_PENDING", await Rules.BrokenAsync(() => Confirm(ticket.MediaId)));
    }

    [Theory]
    [InlineData(71, "image/png")]
    [InlineData(70, "image/jpeg")]
    [InlineData(70, null)]
    public async Task What_arrived_and_is_not_what_was_announced_is_refused(long size, string? type)
    {
        var ticket = (await Announce()).Value;
        store.Objects[files.All[0].StorageKey] = new StoredObject(size, type);

        Assert.Equal("NOT_WHAT_WAS_ANNOUNCED", await Rules.BrokenAsync(() => Confirm(ticket.MediaId)));
        Assert.Equal(MediaState.Pending, files.All[0].State);
    }

    [Fact]
    public async Task A_place_waits_ten_minutes_for_its_bytes()
    {
        var ticket = (await Announce()).Value;
        store.Objects[files.All[0].StorageKey] = new StoredObject(70, "image/png");
        clock.UtcNow += MediaFile.UploadWindow;

        Assert.Equal("UPLOAD_WINDOW_CLOSED", (await Confirm(ticket.MediaId)).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task A_file_is_shown_to_its_city_with_an_address_good_for_five_minutes_and_to_nobody_else()
    {
        var file = await Available();
        Task<Result<MediaView>> Read(FakeTenant tenant) => GetMediaHandler.Handle(new GetMedia(file.Id), tenant, files, store, clock, default);

        var seen = (await Read(FakeTenant.Tehran())).Value;

        Assert.Contains(file.StorageKey, seen.DownloadUrl!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(clock.UtcNow + GetMediaHandler.DownloadWindow, seen.DownloadUrlExpiresOnUtc);
        Assert.DoesNotContain("secret", seen.ToString(), StringComparison.Ordinal);
        Assert.Equal("FILE_NOT_FOUND", (await Read(FakeTenant.Istanbul())).FailureDescriptor!.Identity.Code);
        Assert.Equal("SIGN_IN_REQUIRED", (await Read(FakeTenant.None())).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task A_file_that_is_not_available_yet_is_shown_without_an_address()
    {
        var ticket = (await Announce()).Value;

        var seen = (await GetMediaHandler.Handle(new GetMedia(ticket.MediaId), FakeTenant.Tehran(), files, store, clock, default)).Value;

        Assert.Equal(("Pending", null), (seen.State, seen.DownloadUrl));
    }

    [Fact]
    public async Task Only_who_sent_a_file_deletes_it_and_the_bytes_leave_the_store()
    {
        var file = await Available();

        var bySara = await Delete(file.Id, FakeActor.User("sara", "customer"));
        var byMina = await Delete(file.Id);

        Assert.Equal("NOT_THE_OWNER", bySara.FailureDescriptor!.Identity.Code);
        Assert.Equal("Deleted", byMina.Value.State);
        Assert.Empty(store.Objects);
        Assert.IsType<FileDeleted>(Assert.Single(file.IntegrationEvents));
        Assert.Equal("FILE_NOT_FOUND", (await Delete(file.Id)).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task A_store_that_does_not_answer_changes_nothing_and_is_told_as_something_to_try_again()
    {
        var file = await Available();
        store.IsDown = true;

        var announced = await Announce();
        var deleted = await Delete(file.Id);

        Assert.Equal("STORE_UNAVAILABLE", announced.FailureDescriptor!.Identity.Code);
        Assert.True(deleted.FailureDescriptor!.Retry.IsRetryable);
        Assert.Equal(MediaState.Available, file.State);
        Assert.Single(files.All);
    }

    [Theory]
    [InlineData("front.png", true)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("C:\\pictures\\front.png", false)]
    [InlineData("front\n.png", false)]
    public void A_file_name_holds_no_path(string name, bool accepted) =>
        Assert.Equal(accepted, new ReserveUploadValidator().Validate(new ReserveUpload("restaurant-picture", name, "image/png", 70)).IsValid);
}

public sealed class SettingsTests
{
    [Fact]
    public void The_settings_of_the_store_print_no_key()
    {
        var store = new Tiffin.Media.Infrastructure.Store.S3Options
        {
            Endpoint = new Uri("http://localhost:39000"), Bucket = "tiffin-media", AccessKey = "an-access-key", SecretKey = "a-secret-nobody-may-see"
        };

        Assert.DoesNotContain("a-secret-nobody-may-see", store.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("an-access-key", store.ToString(), StringComparison.Ordinal);
    }
}
