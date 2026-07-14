using Bitspew.Web.Data;
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
builder.Services.AddScoped<PostSubmissionService>();
builder.Services.AddRazorPages();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<BitspewDbContext>().Database.Migrate();
}

app.UsePathBase("/Bitspew");
app.Use(async (context, next) =>
{
    if (!context.Request.PathBase.Equals("/Bitspew", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/Bitspew" + context.Request.Path + context.Request.QueryString);
        return;
    }
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
