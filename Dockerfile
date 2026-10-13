FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ src/
ARG PROJECT
RUN dotnet publish "$PROJECT" -c Release -o /app/publish --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shortener.Web.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shortener.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS analytics
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Shortener.Analytics.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS tests
WORKDIR /src
COPY src/ src/
COPY tests/ tests/
COPY SchwabUrlShortener.slnx .
RUN dotnet restore SchwabUrlShortener.slnx --nologo
