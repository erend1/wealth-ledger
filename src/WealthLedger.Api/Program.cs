using WealthLedger.Api.Endpoints;
using WealthLedger.Api.ErrorHandling;
using WealthLedger.Api.Startup;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddWealthLedgerInfrastructure(builder.Configuration);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<RecordContributionUseCase>();
builder.Services.AddScoped<RecordFundPurchaseUseCase>();
builder.Services.AddScoped<GetPositionUseCase>();
builder.Services.AddScoped<InitializeCoreLedgerUseCase>();

var app = builder.Build();

await app.ApplyDatabaseMigrationsIfEnabledAsync();

app.UseExceptionHandler();
app.MapLedgerEndpoints();
app.MapPositionEndpoints();

if (app.Configuration.GetValue<bool>("Setup:Enabled"))
{
    app.MapSetupEndpoints();
}

app.Run();

public partial class Program;
