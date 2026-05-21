using Workslip.Api.Endpoints;
using Workslip.Api.Middleware;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Schema;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddWorkslipInfrastructure();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<WorkslipSchemaRunner>().ApplyAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
//app.MapDocumentEndpoints();
app.MapOrganizationEndpoints();
app.MapAuthEndpoints();
app.MapJobEndpoints();

app.Run();

