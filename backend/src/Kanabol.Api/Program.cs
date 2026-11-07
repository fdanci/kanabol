using Kanabol.Api.Authentication;
using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Api.Logging;
using Kanabol.Api.Realtime;
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
    .ReadFrom.Services(services)
    .Enrich.With<AccessTokenRedaction>());

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ModelStateProblems.Create)

    // A body that can't be read gets a plain message, not one that names the API's own types.
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsEnrichment.AddTraceIds);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddSwaggerWithBearerToken();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddSignalR();
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

// A connection lasts no longer than the token it was opened with: the browser opens another with a fresh
// one, and IProjectAccess is asked again when it joins its board (SPEC section 9).
app.MapHub<BoardHub>(BoardHub.Path, options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();
