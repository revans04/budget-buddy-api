using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Microsoft.OpenApi.Models;
using FamilyBudgetApi.Services;
using System.IO;
using FamilyBudgetApi.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Firebase setup
// Load Firebase credentials from environment variable instead of a file
var credentialsJson = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS_JSON");
if (string.IsNullOrEmpty(credentialsJson))
{
    Console.WriteLine("Error: GOOGLE_APPLICATION_CREDENTIALS_JSON environment variable not set.");
    throw new InvalidOperationException("Firebase credentials not provided via GOOGLE_APPLICATION_CREDENTIALS_JSON.");
}

try
{
    // Write the credentials to a temporary file
    var tempCredentialsPath = Path.Combine(Path.GetTempPath(), "firebase-service-account.json");
    try
    {
        File.WriteAllText(tempCredentialsPath, credentialsJson);
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", tempCredentialsPath);
        Console.WriteLine($"Firebase credentials loaded from environment variable and written to {tempCredentialsPath}");

        // Initialize Firebase
        FirebaseApp.Create(new AppOptions()
        {
            Credential = GoogleCredential.FromFile(tempCredentialsPath)
        });

        // Add FirestoreDb as a singleton
        var credential = GoogleCredential.FromFile(tempCredentialsPath);
        var projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? builder.Configuration["GoogleCloud:ProjectId"];
        if (string.IsNullOrEmpty(projectId))
        {
            throw new InvalidOperationException("Google Cloud project ID not set. Set GOOGLE_CLOUD_PROJECT environment variable or GoogleCloud:ProjectId in appsettings.json.");
        }
        builder.Services.AddSingleton(FirestoreDb.Create(projectId, new FirestoreClientBuilder
        {
            Credential = credential
        }.Build()));
    }
    finally
    {
        if (File.Exists(tempCredentialsPath))
            File.Delete(tempCredentialsPath);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error initializing Firebase: {ex.Message}");
    throw;
}

// CORS for Vue
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalDomain", policy =>
    {
        // Allow local development URLs for now
        // Update this with your deployed frontend URL after deployment
        policy.WithOrigins("http://localhost:8080",
            "http://localhost",
            "http://localhost:8081",
            "http://family-budget.local:8081",
            "https://budget-buddy-a6b6c.web.app",
            "https://budget-buddy-a6b6c.firebaseapp.com")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add controllers
builder.Services.AddControllers();

// Add services
builder.Services.AddSingleton<FamilyService>();
builder.Services.AddSingleton<BudgetService>();
builder.Services.AddSingleton<FamilyController>();
builder.Services.AddSingleton<BudgetController>();
builder.Services.AddSingleton<UserService>();

// Add Brevo
builder.Services.Configure<FamilyBudgetApi.Models.BrevoSettings>(builder.Configuration.GetSection("Brevo"));

// Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "family-budget-api", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer prefix (e.g., 'Bearer <token>')",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

var app = builder.Build();

// Middleware pipeline
app.UseCors("AllowLocalDomain");

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

// Cloud Run sets the PORT environment variable; use it if available, otherwise default to 8080
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var url = $"http://0.0.0.0:{port}";
Console.WriteLine($"Starting server on {url}");
app.Run(url);