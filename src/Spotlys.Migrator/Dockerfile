# The artifact that actually ships is an EF Core migration bundle -- a self-contained
# native executable (docs/DEVOPS.md §4: no SDK in the final image; ADR-worthy choice
# confirmed with the user for Phase 1). `dotnet ef migrations bundle` needs the SDK and the
# dotnet-ef tool to build; the final image needs neither, only the OS libraries a
# self-contained app links against.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# TARGETARCH is set automatically by BuildKit to whatever platform the final image is
# being built for (arm64 on this dev machine, amd64 on a typical DigitalOcean droplet) --
# a self-contained bundle is architecture-specific, unlike the framework-dependent
# Api/Ingestion images, so this can't be hardcoded to one RID.
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Spotlys.Domain/ src/Spotlys.Domain/
COPY src/Spotlys.Application/ src/Spotlys.Application/
COPY src/Spotlys.Infrastructure/ src/Spotlys.Infrastructure/
COPY src/Spotlys.Migrator/ src/Spotlys.Migrator/
RUN dotnet restore src/Spotlys.Migrator/Spotlys.Migrator.csproj
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="$PATH:/root/.dotnet/tools"
# Bundle creation only needs a syntactically valid connection string (verified locally --
# it never connects at build time), so a placeholder is fine here; the real one is supplied
# to the final image's entrypoint at deploy time via the environment.
ENV ConnectionStrings__Spotlys="Host=placeholder;Database=placeholder;Username=placeholder;Password=placeholder"
RUN dotnet ef migrations bundle \
    --project src/Spotlys.Infrastructure/Spotlys.Infrastructure.csproj \
    --startup-project src/Spotlys.Migrator/Spotlys.Migrator.csproj \
    --self-contained \
    --target-runtime "linux-$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64)" \
    --output /app/efbundle \
    --configuration Release

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app/efbundle .
USER $APP_UID
# Reads ConnectionStrings__Spotlys from the environment, same convention as every other
# service (docker-compose.yml / the k3s Job's env).
ENTRYPOINT ["./efbundle"]
