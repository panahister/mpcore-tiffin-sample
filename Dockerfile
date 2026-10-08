# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime-base
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl python3 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# Install the exact SDK from Microsoft's signed release channel instead of pulling the much
# larger SDK container layer. The SHA-512 values come from the official .NET 10 release index.
# This keeps builds reproducible on both GitHub-hosted x64 runners and Apple Silicon machines.
FROM runtime-base AS build
ARG TARGETARCH
RUN set -eux; \
    case "$TARGETARCH" in \
      amd64) \
        sdk_arch="x64"; \
        sdk_sha512="1033977dd837150e0814cf0c5d5b17ceb63925fda7ba2158b47258a4bd7c048cf82eac3bc1166f3146f53124a3f5fba09db1de1260d2ce96399860303b404b48" \
        ;; \
      arm64) \
        sdk_arch="arm64"; \
        sdk_sha512="a1b45da58e5591fff909a6126ac6bfc1ef9c12bc72c0625f7815e83a82be1a902317ee96926cbbf81324a45c6abf2ed8102a216d0507879cc166159af78d1b77" \
        ;; \
      *) \
        echo "Unsupported Docker target architecture: $TARGETARCH" >&2; \
        exit 1 \
        ;; \
    esac; \
    sdk_archive="/tmp/dotnet-sdk.tar.gz"; \
    curl --fail --location --retry 5 \
      "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.400/dotnet-sdk-10.0.400-linux-${sdk_arch}.tar.gz" \
      --output "$sdk_archive"; \
    printf '%s  %s\n' "$sdk_sha512" "$sdk_archive" | sha512sum --check --strict -; \
    tar --extract --gzip --file "$sdk_archive" --directory /usr/share/dotnet; \
    rm "$sdk_archive"; \
    dotnet --info

# One source build produces every Tiffin business-service binary. The Full Demo runs nine
# containers from this image, each with a different published application as its command.
WORKDIR /source
COPY . .
RUN set -eux; \
    for service in Access Media Restaurants Ordering Payments Kitchen Dispatch Tracking Notifications; do \
      folder="$(printf '%s' "$service" | tr '[:upper:]' '[:lower:]')"; \
      dotnet publish \
        "$folder/src/Tiffin.$service.Api/Tiffin.$service.Api.csproj" \
        --configuration Release \
        --output "/out/$folder" \
        --nologo \
        -p:MPCoreSource=NuGet \
        -p:ContinuousIntegrationBuild=true \
        -p:UseAppHost=false; \
    done

FROM runtime-base AS services
USER $APP_UID
COPY --from=build --chown=$APP_UID:$APP_UID /out/ ./

FROM runtime-base AS seed
WORKDIR /seed
USER $APP_UID
COPY --chown=$APP_UID:$APP_UID scripts/seed-us-poc.py ./scripts/seed-us-poc.py
COPY --chown=$APP_UID:$APP_UID infrastructure/demo-assets ./infrastructure/demo-assets
ENTRYPOINT ["python3", "/seed/scripts/seed-us-poc.py"]
