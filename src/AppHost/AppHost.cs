// The app model. Postgres -> Migrator (one-shot) -> Api -> web.
// Spike C: the Migrator exits, and the Api waits for it to exit 0.
// Spike B: the browser calls a relative /api; Vite proxies it under `aspire run`,
// and at publish the built web app is served by the Api from wwwroot.
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes.Swarm;

var builder = DistributedApplication.CreateBuilder(args);

// Step 7: publish target is Docker Compose. No dashboard in the published stack: it would be
// emitted with no host IP (every interface) and restart: always, listed in no document.
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(false);

var postgres = builder.AddPostgres("postgres");

// S-25b: the named volume is OPT-IN in run mode. DistributedApplicationTestingBuilder randomises
// ports and nothing else, so an unconditional WithDataVolume would mount the developer's real
// data into every test run. `aspire run` sets Postgres:DataVolume from the launch profile (which
// it always applies - Spike A); the test builder leaves it unset, and tests/E2E asserts that.
// PUBLISH always gets one: a deployed database must outlive its container.
// Named explicitly; `_dev`, not a bare `portfolio`: a PRD name must never be a substring of the
// DEV one (environments.md banner).
var volume = builder.Configuration["Postgres:DataVolume"] is { Length: > 0 } configured ? configured
    : builder.ExecutionContext.IsPublishMode ? "portfolio-dev-pgdata"
    : null;
if (volume is not null)
    postgres.WithDataVolume(volume);

postgres.PublishAsDockerComposeService((_, service) =>
{
    // The publisher writes no healthcheck, and degrades every WaitFor to service_started
    // (its service_healthy branch is commented out). Without this the Migrator can connect
    // before Postgres accepts connections.
    service.Healthcheck = new()
    {
        Test = ["CMD-SHELL", "pg_isready -U postgres"],
        Interval = "5s",
        Timeout = "3s",
        Retries = 10,
        StartPeriod = "10s",
    };
    service.Restart = "unless-stopped";
    service.Deploy = MemoryLimit("256M");
});

var db = postgres.AddDatabase("portfolio", databaseName: "portfolio_dev");

var migrator = builder.AddProject<Projects.Migrator>("migrator")
    .WithReference(db)
    .WaitFor(db)
    .PublishAsDockerComposeService((_, service) =>
        // Overwrite the service_started entry the publisher wrote. No restart policy: this is
        // a one-shot, and the Api waits for it to EXIT (service_completed_successfully).
        service.DependsOn["postgres"] = new() { Condition = "service_healthy" });

// Slice 03 step 1 (D2): the first-run setup code, a secret parameter the AppHost generates.
// 29 characters from Aspire's lower-case alphabet, which already leaves out i, l and o: about
// 131 bits, and nothing to misread (gate GT-W, from aspire@v13.6.1 src/Shared/PasswordGenerator.cs).
// Not persisted: under `aspire run` a fresh code is generated on every start, and `aspire deploy`
// writes the resolved value to .env.<environment>. The Api accepts it only while no user exists,
// and refuses any code already spent (used_setup_code), so a stale value cannot enrol anyone.
var setupCode = builder.AddParameter("setup-code",
    new GenerateParameterDefault { MinLength = 29, Lower = true, Upper = false, Numeric = false, Special = false },
    secret: true);

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(db)
    .WithEnvironment("Enrolment__SetupCode", setupCode)
    // Slice 03 step 7 (C3-9): Aspire's publish default trusts X-Forwarded-For and -Proto from any
    // client, so every caller would choose its own address and scheme. There is no proxy to trust.
    .DisableForwardedHeaders()
    .WaitForCompletion(migrator)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .PublishAsDockerComposeService((_, service) =>
    {
        // DoD 3: the Api on LOOPBACK. The publisher writes a bare "${API_PORT}" (no host IP,
        // so Docker binds every interface), and a compose override cannot remove it - `ports`
        // appends. Kestrel still binds 0.0.0.0 inside the container (ASPNETCORE_HTTP_PORTS);
        // loopback is purely a property of this publish string.
        service.Ports = service.Ports.Select(p => $"127.0.0.1:{p}:{p}").ToList();
        service.Restart = "unless-stopped";
        service.Deploy = MemoryLimit("512M");
    });

var web = builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api);

api.PublishWithContainerFiles(web, "wwwroot");

// Slice 03 step 7 (D3, C3-11): the data-protection key ring, the root of trust for every cookie and
// token, outlives the container. Declared as a mount annotation, so the compose file declares the
// volume too (a volume added only in the compose callback is never declared: SRC-7). Mounted AT
// /home/app, which the image's `app` user (UID 1654) owns: Docker initialises an empty named
// volume from the mount point's own ownership, where a fresh path would be root's and unwritable.
// Under `aspire run` the Api is a process, and keeps its keys in the user profile.
if (builder.ExecutionContext.IsPublishMode)
{
    api.WithAnnotation(new ContainerMountAnnotation("portfolio-dev-keys", "/home/app", ContainerMountType.Volume, isReadOnly: false));
    api.WithEnvironment("DataProtection__KeyPath", "/home/app/keys");
}

// Slice 03 step 2: the origins the Api's passkey check accepts (C3-23). Under `aspire run` the page
// is the Vite dev server's, on a port Aspire allocates; published, the Api serves the page itself,
// on the port the compose file publishes on loopback (SRC-12).
if (builder.ExecutionContext.IsPublishMode)
    api.WithEnvironment("Auth__AllowedOrigins", ReferenceExpression.Create($"http://localhost:{api.GetEndpoint("http").Property(EndpointProperty.Port)}"));
else
    api.WithEnvironment("Auth__AllowedOrigins", web.GetEndpoint("http"));

builder.Build().Run();

// S-34, deferred by slice 01 to "the first slice that runs an ingest batch" - without limits a
// mis-sized ingest batch takes the host down. Measured 2026-10-18 on the published stack with no
// limit, a 10,000-row import, the same file again, every row restated and the widest net-worth
// query: peaks (cgroup memory.peak) api 234.5 MiB, postgres 126.3 MiB. The limits are about twice
// that. The api's unconstrained peak overstates its need - .NET grows its heap into whatever is
// free, and under a limit caps it at 75% - and the same load was re-run under these limits and
// passed (slice-02.md step 7). Revisit when the import limit or the row limit changes.
static Deploy MemoryLimit(string memory) => new() { Resources = new() { Limits = new() { Memory = memory } } };
