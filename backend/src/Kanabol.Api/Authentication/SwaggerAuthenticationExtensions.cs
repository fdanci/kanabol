using Microsoft.OpenApi;

namespace Kanabol.Api.Authentication;

public static class SwaggerAuthenticationExtensions
{
    private const string SchemeName = "Bearer";

    /// <summary>Swagger's Authorize button: paste the accessToken that POST /api/auth/login returns.</summary>
    public static IServiceCollection AddSwaggerWithBearerToken(this IServiceCollection services) =>
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The accessToken from POST /api/auth/login, without the word Bearer.",
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
            });
        });
}
