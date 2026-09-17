FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Spotlys.Domain/ src/Spotlys.Domain/
COPY src/Spotlys.Application/ src/Spotlys.Application/
COPY src/Spotlys.Infrastructure/ src/Spotlys.Infrastructure/
COPY src/Spotlys.Ingestion/ src/Spotlys.Ingestion/
RUN dotnet publish src/Spotlys.Ingestion/Spotlys.Ingestion.csproj -c Release -o /app --no-self-contained

# Not just dotnet/runtime: Serilog.AspNetCore pulls in the ASP.NET Core shared framework
# at runtime even though this is a Worker Service, not a web app (verified locally --
# "dotnet/runtime" alone fails to launch with a missing-framework error).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata libkrb5-3 \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Spotlys.Ingestion.dll"]
