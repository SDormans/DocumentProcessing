FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DocumentProcessing.Domain/DocumentProcessing.Domain.csproj DocumentProcessing.Domain/
COPY DocumentProcessing.Application/DocumentProcessing.Application.csproj DocumentProcessing.Application/
COPY DocumentProcessing.Infrastructure/DocumentProcessing.Infrastructure.csproj DocumentProcessing.Infrastructure/
COPY DocumentProcessing.Api/DocumentProcessing.Api.csproj DocumentProcessing.Api/
RUN dotnet restore DocumentProcessing.Api/DocumentProcessing.Api.csproj

COPY DocumentProcessing.Domain/ DocumentProcessing.Domain/
COPY DocumentProcessing.Application/ DocumentProcessing.Application/
COPY DocumentProcessing.Infrastructure/ DocumentProcessing.Infrastructure/
COPY DocumentProcessing.Api/ DocumentProcessing.Api/
RUN dotnet publish DocumentProcessing.Api/DocumentProcessing.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
RUN mkdir -p /app/data/storage && chown -R app:app /app/data
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "DocumentProcessing.Api.dll"]
