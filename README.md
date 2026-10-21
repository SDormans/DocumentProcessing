# Document Processing Platform

ASP.NET Core Web API voor veilig uploaden, valideren en opslaan van PDF-documenten.
Dit is Stap 1 van het AI Document Processing Platform — OCR, AI-extractie en
background processing komen in latere fasen.

## Projectstructuur

| Project | Verantwoordelijkheid |
|---------|---------------------|
| `DocumentProcessing.Domain` | Domeinmodellen (`Document`, `DocumentStatus`) en interfaces (`IFileStorage`, `IDocumentRepository`) |
| `DocumentProcessing.Application` | DTO's, `DocumentService`, validatie en uitzonderingen |
| `DocumentProcessing.Infrastructure` | EF Core `AppDbContext` (PostgreSQL), `LocalFileStorage`, migraties |
| `DocumentProcessing.Api` | Web API (`POST/GET/GET-by-id` op `/api/documents`), foutafhandeling, OpenAPI/Swagger |
| `DocumentProcessing.UnitTests` | Unit tests voor validatie en servicelogica |
| `DocumentProcessing.IntegrationTests` | Integration tests voor de API-controller |

## Vereisten

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 16 (via Docker Compose of lokaal)
- Docker (optioneel, voor PostgreSQL)

## Configuratie

1. Kopieer `appsettings.example.json` naar `DocumentProcessing.Api/appsettings.json`:
   ```powershell
   Copy-Item DocumentProcessing.Api/appsettings.example.json DocumentProcessing.Api/appsettings.json
   ```

2. Pas de connection string in `appsettings.json` aan met je eigen database-credentialen.
   De `appsettings.json` bevat gevoelige informatie en wordt **niet** in versiebeheer opgenomen (zie `.gitignore`).

3. De `docker-compose.yml` gebruikt de default wachtwoord `ChangeMe`. Pas dit aan in een production-omgeving.

## Database

### PostgreSQL starten (Docker)

```powershell
docker-compose up -d
```

### EF Core migraties

Een initiële migratie is aanwezig in `DocumentProcessing.Infrastructure/Data/Migrations/`.

Pas de migratie toe op de database:

```powershell
dotnet ef database update --project DocumentProcessing.Infrastructure --startup-project DocumentProcessing.Api
```

Een nieuwe migratie toevoegen:

```powershell
dotnet ef migrations add <Naam> --project DocumentProcessing.Infrastructure --startup-project DocumentProcessing.Api
```

## Applicatie uitvoeren

```powershell
dotnet run --project DocumentProcessing.Api
```

De API is beschikbaar op:
- HTTPS: `https://localhost:5001`
- HTTP: `http://localhost:5000`
- Swagger UI: `https://localhost:5001/swagger`

## API Endpoints

| Methode | Endpoint | Beschrijving |
|---------|----------|--------------|
| `POST` | `/api/documents` | Upload een PDF-bestand (`multipart/form-data`) |
| `GET` | `/api/documents` | Lijst met alle geüploade documenten |
| `GET` | `/api/documents/{id}` | Een enkel document ophalen op id |

### Upload validatie

- Bestandslengte moet groter zijn dan 0
- Bestandsgrootte mag niet overschrijden `DocumentService:MaxFileSizeBytes` (standaard 10 MB)
- Bestandsnaam moet eindigen op `.pdf`
- PDF-signatuur (`%PDF-`) wordt gecontroleerd in de bestandsinhoud — niet uitsluitend de extensie of Content-Type header

## Tests uitvoeren

```powershell
dotnet test DocumentProcessing.slnx
```

Unit tests (13) en integration tests (6) worden uitgevoerd. Voor integration tests moet PostgreSQL bereikbaar zijn op de configured connection string.

## Architectuur

- Modulaire oplossing: Domain, Application, Infrastructure, Api
- Dependency Injection voor alle services
- Async/await voor alle I/O-operaties
- CancellationToken ondersteuning
- Uniforme foutafhandeling via middleware
- DTO's voor API-invoer en -uitvoer (geen EF Core-entiteiten rechtstreeks)

## Licentie

Privé project — geen openbare licentie.