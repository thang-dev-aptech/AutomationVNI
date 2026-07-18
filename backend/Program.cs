using System.Text;
using Backend.Data;
using Backend.Modules.ApiLog;
using Backend.Modules.Auth;
using Backend.Modules.Category;
using Backend.Modules.GenerationJob;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaEmbedding;
using Backend.Modules.PageContext;
using Backend.Modules.Post;
using Backend.Modules.PublishLog;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialConnection;
using Backend.Shared;
using Backend.Shared.Ai;
using Backend.Shared.Meta;
using Backend.Shared.SocialPublish;
using Backend.Shared.DevSeed;
using Backend.Shared.Middleware;
using Backend.Shared.Repositories;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SeedSettings>(builder.Configuration.GetSection("Seed"));
builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
builder.Services.Configure<GenerationWorkerOptions>(builder.Configuration.GetSection("GenerationWorker"));
builder.Services.Configure<DevSeedOptions>(builder.Configuration.GetSection("DevSeed"));
builder.Services.Configure<AiProvidersOptions>(builder.Configuration.GetSection("AiProviders"));
builder.Services.Configure<SocialPublishOptions>(builder.Configuration.GetSection("SocialPublish"));
builder.Services.Configure<MetaOAuthOptions>(builder.Configuration.GetSection("MetaOAuth"));

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");

if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
    throw new InvalidOperationException(
        "Jwt:SecretKey is required. Set via user-secrets, environment variable Jwt__SecretKey, or appsettings.Development.json for local dev.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IImageOverlayService, ImageOverlayService>();

// Module repositories
builder.Services.AddScoped<CategoryRepository>();
builder.Services.AddScoped<SocialChannelRepository>();
builder.Services.AddScoped<SocialConnectionRepository>();
builder.Services.AddScoped<PageContextRepository>();
builder.Services.AddScoped<Backend.Modules.PromptTemplate.PromptTemplateRepository>();
builder.Services.AddScoped<PostRepository>();
builder.Services.AddScoped<PostWorkflowService>();
builder.Services.AddScoped<MediaAssetRepository>();
builder.Services.AddScoped<PostMediaRepository>();
builder.Services.AddScoped<GenerationJobRepository>();
builder.Services.AddScoped<GenerationJobPipelineService>();
builder.Services.AddScoped<IPublishPipelineService, PublishPipelineService>();
builder.Services.AddScoped<PublishLogRepository>();
builder.Services.AddHostedService<Backend.Shared.Scheduler.ScheduledPostPublisherService>();
builder.Services.AddHostedService<Backend.Shared.Generation.PostGenerationWorker>();
builder.Services.AddScoped<MediaEmbeddingRepository>();
builder.Services.AddScoped<ApiLogRepository>();
builder.Services.AddScoped<IDevDataSeeder, DevDataSeeder>();
builder.Services.AddHttpClient<IAiTextGenerationService, OpenAiCompatibleTextGenerationService>();
builder.Services.Configure<AiImageProvidersOptions>(builder.Configuration.GetSection("AiImageProviders"));
builder.Services.AddHttpClient<IAiImageGenerationService, GeminiImageGenerationService>();
builder.Services.AddSingleton<MockSocialPublishService>();
builder.Services.AddHttpClient<FacebookPagePublishService>((sp, client) =>
{
    var fb = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SocialPublishOptions>>().Value.Facebook;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, fb.TimeoutSeconds));
});
builder.Services.AddScoped<ISocialPublishService, SocialPublishService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(nameof(MetaOAuthService));
builder.Services.AddScoped<MetaPageSyncService>();
builder.Services.AddScoped<IMetaOAuthService, MetaOAuthService>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.SuppressModelStateInvalidFilter = false);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
});

builder.Services.AddOpenApi();

// Behind Railway/Render/Fly, TLS terminates at the edge and the container receives HTTP.
// Honor X-Forwarded-Proto/For so the app sees the original https scheme (correct absolute URLs,
// no http→https redirect loop).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

using (var migrateScope = app.Services.CreateScope())
{
    var db = migrateScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

var devSeedOptions = app.Configuration.GetSection("DevSeed").Get<DevSeedOptions>();
if (devSeedOptions?.Enabled == true && !app.Environment.IsDevelopment())
    app.Logger.LogWarning("DevSeed is enabled outside Development — disable for production deployments");

await IdentitySeeder.SeedAsync(app.Services);

using (var seedScope = app.Services.CreateScope())
{
    var devSeeder = seedScope.ServiceProvider.GetRequiredService<IDevDataSeeder>();
    await devSeeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevCors");
    app.MapOpenApi();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();

// Serve the built React SPA (Vite output at wwwroot/dist) from the site root.
// Assets are referenced as /assets/... so the file provider must point at the dist folder.
var spaRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "dist");
var spaAvailable = Directory.Exists(spaRoot);
if (spaAvailable)
{
    var spaFiles = new PhysicalFileProvider(spaRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = spaFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = spaFiles });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseMiddleware<ApiLogMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// SPA fallback for client-side routes (e.g. /platforms, /posts/:id) → serve dist/index.html.
if (spaAvailable)
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(spaRoot)
    });
else
    app.MapFallbackToFile("index.html");

app.Run();
