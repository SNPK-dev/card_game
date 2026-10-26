FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Game.sln ./
COPY src/Game.Api/Game.Api.csproj src/Game.Api/
COPY src/Game.Application/Game.Application.csproj src/Game.Application/
COPY src/Game.Domain/Game.Domain.csproj src/Game.Domain/
COPY src/Game.Generator/Game.Generator.csproj src/Game.Generator/
COPY src/Game.Infrastructure/Game.Infrastructure.csproj src/Game.Infrastructure/
COPY src/Game.Shared/Game.Shared.csproj src/Game.Shared/
RUN dotnet restore src/Game.Api/Game.Api.csproj

COPY src/ src/
RUN dotnet publish src/Game.Api/Game.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__RootPath=/var/lib/card-game
ENV Api__MaxRequestBodyBytes=1048576
ENV Api__WriteRateLimit__PermitLimit=60
ENV Api__WriteRateLimit__WindowSeconds=60
RUN apt-get update \
  && apt-get install -y --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/* \
  && mkdir -p /var/lib/card-game
VOLUME ["/var/lib/card-game"]
EXPOSE 8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "Game.Api.dll"]
