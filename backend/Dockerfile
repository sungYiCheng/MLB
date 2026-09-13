# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY MlbAi.sln ./
COPY src/MlbAi.Api/MlbAi.Api.csproj src/MlbAi.Api/
COPY src/MlbAi.Application/MlbAi.Application.csproj src/MlbAi.Application/
COPY src/MlbAi.Infrastructure/MlbAi.Infrastructure.csproj src/MlbAi.Infrastructure/

RUN dotnet restore src/MlbAi.Api/MlbAi.Api.csproj

COPY . ./
RUN dotnet publish src/MlbAi.Api/MlbAi.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "MlbAi.Api.dll"]
