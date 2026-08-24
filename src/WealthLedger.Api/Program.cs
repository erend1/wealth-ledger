using WealthLedger.Api.Endpoints;
using WealthLedger.Api.ErrorHandling;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Positions;
using WealthLedger.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddWealthLedgerInfrastructure(builder.Configuration);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<RecordContributionUseCase>();
builder.Services.AddScoped<RecordFundPurchaseUseCase>();
builder.Services.AddScoped<GetPositionUseCase>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapLedgerEndpoints();
app.MapPositionEndpoints();

app.Run();

public partial class Program;
