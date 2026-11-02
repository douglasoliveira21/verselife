FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY global.json ./
COPY backend/LifeVerse.Api.csproj backend/
RUN dotnet restore backend/LifeVerse.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/LifeVerse.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
RUN addgroup -S lifeverse && adduser -S lifeverse -G lifeverse
COPY --from=build /app .
USER lifeverse
EXPOSE 8080
ENTRYPOINT ["dotnet", "LifeVerse.Api.dll"]
