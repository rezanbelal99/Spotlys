# Multi-stage: SDK to build, slim ASP.NET runtime to run. No build toolchain in the final
# image (docs/DEVOPS.md §4).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Spotlys.Domain/ src/Spotlys.Domain/
COPY src/Spotlys.Application/ src/Spotlys.Application/
COPY src/Spotlys.Infrastructure/ src/Spotlys.Infrastructure/
COPY src/Spotlys.Api/ src/Spotlys.Api/
RUN dotnet publish src/Spotlys.Api/Spotlys.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# TimeProvider + Europe/Oslo tariff math need the IANA tzdata (docs/CLAUDE.md "things that
# have bitten this project before").
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Spotlys.Api.dll"]
