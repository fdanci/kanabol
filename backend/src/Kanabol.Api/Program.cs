using Kanabol.Api.Authentication;
using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Api.Logging;
using Kanabol.Application;
using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
}

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnrichment.AddTraceIds);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddJwtAuthentication();
builder.Services.AddPermissionAuthorization();
builder.Services.AddLoginRateLimit();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<UserIdLogContextMiddleware>();
app.UseMiddleware<PasswordChangeRequiredMiddleware>();
app.UseAuthorization();

// Every controller needs a signed-in user unless it says [AllowAnonymous], so forgetting an attribute closes an endpoint.
app.MapControllers().RequireAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
