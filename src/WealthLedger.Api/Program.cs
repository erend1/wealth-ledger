using WealthLedger.Api.Endpoints;
using WealthLedger.Api.ErrorHandling;
using WealthLedger.Api.Startup;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Positions;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure;
using WealthLedger.Infrastructure.LocalData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddWealthLedgerInfrastructure(
    builder.Configuration,
    new LocalDataRuntimeContext(
        builder.Environment.EnvironmentName,
        builder.Environment.ContentRootPath));
builder.Services.AddScoped<RecordContributionUseCase>();
builder.Services.AddScoped<RecordFundPurchaseUseCase>();
builder.Services.AddScoped<GetPositionUseCase>();
builder.Services.AddScoped<InitializeCoreLedgerUseCase>();
builder.Services.AddScoped<GetLedgerTransactionUseCase>();
builder.Services.AddScoped<PreviewPostedTransactionReversalUseCase>();
builder.Services.AddScoped<ReversePostedTransactionUseCase>();

var app = builder.Build();

var hostingFailure = LocalHostingPolicy.Validate(app.Configuration);

if (hostingFailure is not null)
{
    WriteStartupFailure(hostingFailure);
    return;
}

var startupResult = await app.Services
    .GetRequiredService<ILocalApiDatabaseStartup>()
    .StartAsync();

if (!startupResult.Succeeded)
{
    WriteStartupFailure(startupResult.Failure!);
    return;
}

app.Logger.LogInformation(
    "Resolved authoritative database path: {DatabasePath}",
    startupResult.Value!.DatabasePath);

app.UseExceptionHandler();
app.MapLedgerEndpoints();
app.MapPositionEndpoints();

if (app.Configuration.GetValue<bool>("Setup:Enabled"))
{
    app.MapSetupEndpoints();
}

app.Run();

static void WriteStartupFailure(LocalDataFailure failure)
{
    Console.Error.WriteLine(
        $"STARTUP {failure.Category.ToString().ToUpperInvariant()}: {failure.Message}");
    Environment.ExitCode = (int)failure.Category;
}

public partial class Program;
