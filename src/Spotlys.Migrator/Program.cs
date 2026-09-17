using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spotlys.Infrastructure;

// Applies pending EF Core migrations and exits. Run as the init container/Job in
// docker-compose and k3s -- never at API startup (docs/ARCHITECTURE.md §4). CI compiles
// this into a self-contained migration bundle (`dotnet ef migrations bundle`); that bundle,
// not this project's own image, is what actually ships (docs/DEVOPS.md §4: no SDK in the
// final image).
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSpotlysInfrastructure(builder.Configuration);

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<SpotlysDbContext>();

Console.WriteLine("Spotlys.Migrator: applying pending migrations...");
await dbContext.Database.MigrateAsync().ConfigureAwait(false);
Console.WriteLine("Spotlys.Migrator: done.");
