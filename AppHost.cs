var builder = DistributedApplication.CreateBuilder(args);

const string huskerMiniPath = "../husker-mini";
const string rudyPath = "../rudy";
const string vahsPath = "../vahs-software";
const string ycOrganizerPath = "../choreographd";

// ---------- husker-mini: Aspire-managed Postgres (no pre-existing container/data) ----------
var huskerDbUser = builder.AddParameter("husker-db-user", value: "husker");
var huskerDbPassword = builder.AddParameter("husker-db-password", value: "husker", secret: true);
var huskerPg = builder.AddPostgres("husker-postgres", huskerDbUser, huskerDbPassword)
    .WithImageTag("16")
    .WithDataVolume("husker-mini_postgres_data");
huskerPg.AddDatabase("husker-mini-db", databaseName: "husker_mini");
var huskerConnStr = ReferenceExpression.Create(
    $"postgresql://{huskerDbUser}:{huskerDbPassword}@{huskerPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{huskerPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}/husker_mini");

// ---------- rudy: Aspire-managed Postgres, reusing existing docker volume ----------
// Before first run: `cd rudy && docker compose down` (keep, don't -v) to free port 5700 / the volume.
var rudyDbUser = builder.AddParameter("rudy-db-user", value: "rudy");
var rudyDbPassword = builder.AddParameter("rudy-db-password", value: "rudy", secret: true);
var rudyPg = builder.AddPostgres("rudy-postgres", rudyDbUser, rudyDbPassword)
    .WithImageTag("17")
    .WithDataVolume("rudy_rudy_postgres_data");
rudyPg.AddDatabase("rudy");
var rudyConnStr = ReferenceExpression.Create(
    $"postgres://{rudyDbUser}:{rudyDbPassword}@{rudyPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{rudyPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}/rudy");

// ---------- vahs-software: existing container (no named volume, 6+ weeks of data in its writable layer) ----------
// Left running via its own `docker compose up -d` — NOT managed by Aspire to avoid any risk to unbacked data.
var vahsConnStr = builder.AddParameter("vahs-db-connection-string",
    value: "postgresql://pgadmin:password@localhost:5439/docker", secret: true);

// ---------- yc-organizer: Aspire-managed Postgres, reusing existing docker volume ----------
// Before first run: `cd yc-organizer && docker compose down` (keep, don't -v) — container is currently stopped anyway.
var ycDbUser = builder.AddParameter("ycorg-db-user", value: "postgres");
var ycDbPassword = builder.AddParameter("ycorg-db-password", value: "postgres", secret: true);
var ycPg = builder.AddPostgres("ycorg-postgres", ycDbUser, ycDbPassword)
    .WithImageTag("16")
    .WithDataVolume("yc-organize_postgres-data");
ycPg.AddDatabase("ycorg-db", databaseName: "yc_organize");
var ycConnStr = ReferenceExpression.Create(
    $"postgres://{ycDbUser}:{ycDbPassword}@{ycPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{ycPg.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}/yc_organize");

// ---------- husker-mini: web + 2 background workers (npm) ----------
builder.AddExecutable("husker-mini-web", "npx", huskerMiniPath, "next", "dev", "-p", "3010")
    .WithHttpEndpoint(port: 3010, targetPort: 3010, env: "PORT", isProxied: false)
    .WithEnvironment("DATABASE_URL", huskerConnStr)
    .WithReference(huskerPg)
    .WaitFor(huskerPg)
    .WithExternalHttpEndpoints()
    .WithParentRelationship(huskerPg);

builder.AddExecutable("husker-mini-prediction-worker", "npx", huskerMiniPath, "tsx", "watch", "-r", "dotenv/config", "src/workers/prediction-worker.ts")
    .WithEnvironment("DATABASE_URL", huskerConnStr)
    .WaitFor(huskerPg)
    .WithParentRelationship(huskerPg);

builder.AddExecutable("husker-mini-settlement-worker", "npx", huskerMiniPath, "tsx", "watch", "-r", "dotenv/config", "src/workers/settlement-worker.ts")
    .WithEnvironment("DATABASE_URL", huskerConnStr)
    .WaitFor(huskerPg)
    .WithParentRelationship(huskerPg);

// ---------- rudy: single Next.js app (bun) ----------
builder.AddExecutable("rudy-web", "bun", rudyPath, "run", "dev", "--", "-p", "3011")
    .WithHttpEndpoint(port: 3011, targetPort: 3011, env: "PORT", isProxied: false)
    .WithEnvironment("DATABASE_URL", rudyConnStr)
    .WaitFor(rudyPg)
    .WithExternalHttpEndpoints()
    .WithParentRelationship(rudyPg);

// ---------- vahs-software: single Next.js app (bun) ----------
builder.AddExecutable("vahs-software-web", "bun", vahsPath, "run", "dev", "--", "-p", "3012")
    .WithHttpEndpoint(port: 3012, targetPort: 3012, env: "PORT", isProxied: false)
    .WithEnvironment("DATABASE_URL", vahsConnStr)
    .WithExternalHttpEndpoints();

// ---------- yc-organizer: single Next.js app (bun) ----------
builder.AddExecutable("yc-organizer-web", "bun", ycOrganizerPath, "run", "dev", "--", "-p", "3013")
    .WithHttpEndpoint(port: 3013, targetPort: 3013, env: "PORT", isProxied: false)
    .WithEnvironment("DATABASE_URL", ycConnStr)
    .WaitFor(ycPg)
    .WithExternalHttpEndpoints()
    .WithParentRelationship(ycPg);

builder.Build().Run();
