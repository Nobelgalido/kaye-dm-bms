using KayeDM.Application.Buses;
using KayeDM.Application.Closing;
using KayeDM.Application.Dashboard;
using KayeDM.Application.Expenses;
using KayeDM.Application.Inventory;
using KayeDM.Application.Menu;
using KayeDM.Application.Orders;
using KayeDM.Infrastructure.Buses;
using KayeDM.Infrastructure.Closing;
using KayeDM.Infrastructure.Dashboard;
using KayeDM.Infrastructure.Data;
using KayeDM.Infrastructure.Expenses;
using KayeDM.Infrastructure.Identity;
using KayeDM.Infrastructure.Inventory;
using KayeDM.Infrastructure.Menu;
using KayeDM.Infrastructure.Orders;
using KayeDM.Infrastructure.Seeding;
using KayeDM.Web.Components;
using ApexCharts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("KayeDmBms")));

builder.Services.AddScoped<IMenuItemService, MenuItemService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IBusService, BusService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IClosingService, ClosingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddApexCharts();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
    .AddIdentityCookies(cookieOptions =>
    {
        // Defaults are /Account/Login and /Account/AccessDenied -- neither
        // exists in this app. A full-page request for a route the signed-in
        // user's role can't reach is rejected by ASP.NET Core's authorization
        // middleware (via the page's [Authorize(Roles=...)] endpoint metadata)
        // before Blazor's router ever runs, so it's this AccessDeniedPath --
        // not Routes.razor's NotAuthorized branch -- that actually renders
        // the friendly access-denied view for that case.
        cookieOptions.ApplicationCookie?.Configure(options =>
        {
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/access-denied";
        });
    });

builder.Services.AddAuthorizationCore();

builder.Services.AddIdentityCore<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using (var migrationDb = await dbContextFactory.CreateDbContextAsync())
    {
        await migrationDb.Database.MigrateAsync();
    }

    var expenseService = scope.ServiceProvider.GetRequiredService<IExpenseService>();
    await expenseService.SeedDefaultCategoriesAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);

    // Docker's one-liner start: seed the 30-day demo dataset the first time
    // the container sees an empty database, never again after that, so a
    // container restart doesn't wipe whatever the seeded data has become
    // through actual use (SeedDataGenerator.RunAsync always wipes first).
    if (builder.Configuration.GetValue("SEED_ON_START", false))
    {
        await using var seedDb = await dbContextFactory.CreateDbContextAsync();
        if (!await seedDb.MenuItems.AnyAsync())
        {
            await new SeedDataGenerator().RunAsync(seedDb);
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/do-login", async (HttpContext context, SignInManager<IdentityUser> signInManager) =>
{
    var form = await context.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);

    var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
    return result.Succeeded
        ? Results.LocalRedirect(target)
        : Results.LocalRedirect($"/account/login?error=1&returnUrl={Uri.EscapeDataString(target)}");
});

app.MapPost("/account/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/account/login");
});

if (args.Contains("--seed"))
{
    using var seedScope = app.Services.CreateScope();
    var dbContextFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var seedDb = await dbContextFactory.CreateDbContextAsync();
    await new SeedDataGenerator().RunAsync(seedDb);
    return;
}

app.Run();
