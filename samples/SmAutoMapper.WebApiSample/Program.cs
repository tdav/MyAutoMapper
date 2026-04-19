using Microsoft.EntityFrameworkCore;
using SmAutoMapper.WebApiSample.Data;
using SmAutoMapper.WebApiSample.Profiles;
using SmAutoMapper.Extensions;

var builder = WebApplication.CreateBuilder(args);

// EF Core — SQLite in-memory (self-contained demo, no external DB needed)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=sample.db"));

// SmAutoMapper — auto-scan assembly for all MappingProfile subclasses.
// IL3050/IL2026 are suppressed because this sample targets JIT/non-AOT runtime;
// consumers enabling AOT should configure a static mapping surface instead.
#pragma warning disable IL3050, IL2026
builder.Services.AddMapping(typeof(Program).Assembly);
#pragma warning restore IL3050, IL2026

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(); // Added SwaggerGen

var app = builder.Build();

// Seed test data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    SeedData.Initialize(db);
}

app.UseSwagger();
app.UseSwaggerUI();

// Redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapControllers();

app.Run();
