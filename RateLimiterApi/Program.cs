using RateLimiterApi.Configuration;
using RateLimiterApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<RateLimitConfig>(builder.Configuration.GetSection("RateLimiting"));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseMiddleware<RateLimitingMiddleware>();
app.UseHttpsRedirection();
app.MapControllers();

app.Run();
