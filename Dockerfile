FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY LifeLog.slnx ./
COPY src/LifeLog.Api/LifeLog.Api.csproj src/LifeLog.Api/
COPY src/LifeLog.Application/LifeLog.Application.csproj src/LifeLog.Application/
COPY src/LifeLog.Domain/LifeLog.Domain.csproj src/LifeLog.Domain/
COPY src/LifeLog.Infrastructure/LifeLog.Infrastructure.csproj src/LifeLog.Infrastructure/
RUN dotnet restore src/LifeLog.Api/LifeLog.Api.csproj

COPY . .
RUN dotnet publish src/LifeLog.Api/LifeLog.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends git curl ca-certificates gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key \
        | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_22.x nodistro main" \
        > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && npm install -g @google/gemini-cli \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY scripts/gemini-wrapper.sh /app/scripts/gemini-wrapper.sh
RUN chmod +x /app/scripts/gemini-wrapper.sh

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "LifeLog.Api.dll"]
