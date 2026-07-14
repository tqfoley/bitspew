using Bitspew.Web.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("BitspewDb")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'BitspewDb' is not set. Copy the .NET (Npgsql) connection string from your " +
        "Neon dashboard and store it with: dotnet user-secrets set \"ConnectionStrings:BitspewDb\" \"<value>\" " +
        "(or set the ConnectionStrings__BitspewDb environment variable).");
}

builder.Services.AddDbContext<BitspewDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRazorPages();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BitspewDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Trust the reverse proxy's scheme/client-ip headers so https detection works behind it.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// The site is publicly rooted at /bitspew. Locally the prefix arrives intact and
// UsePathBase strips it; the production proxy strips it before forwarding, so reapply
// it as the path base either way. Generated URLs then always carry /bitspew, and a
// stripping proxy can't cause a redirect loop.
app.UsePathBase("/bitspew");
app.Use((context, next) =>
{
    if (!context.Request.PathBase.HasValue)
        context.Request.PathBase = "/bitspew";
    if (!context.Request.Path.HasValue)
    {
        // Exact "/bitspew" leaves an empty path, which routing won't match; normalize it.
        context.Response.Redirect($"{context.Request.PathBase}/{context.Request.QueryString}");
        return Task.CompletedTask;
    }
    return next();
});

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
