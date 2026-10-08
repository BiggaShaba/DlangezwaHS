using Azure.Identity;
using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ─── Bootstrap Serilog early ──────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog full config ──────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .WriteTo.Console()
        .WriteTo.File("logs/dlangezwa-.txt", rollingInterval: RollingInterval.Day));

    // ─── Azure Key Vault (optional – only if KeyVaultUri is configured) ───────
    var kvUri = builder.Configuration["KeyVaultUri"];
    if (!string.IsNullOrEmpty(kvUri))
    {
        builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
        Log.Information("Azure Key Vault configured: {Uri}", kvUri);
    }

    // ─── Database ─────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<ApplicationDbContext>(opts =>
        opts.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sql => sql.EnableRetryOnFailure(3)
        ));

    // ─── Data Protection ──────────────────────────────────────────────────────
    // Containers (e.g. Render) lose their file system on every restart, so keep the
    // cookie/antiforgery encryption keys in the database. Otherwise each restart
    // invalidates existing logins and any form opened before it.
    builder.Services.AddDataProtection()
        .SetApplicationName("DlangezwaHS")
        .PersistKeysToDbContext<ApplicationDbContext>();

    // ─── Identity ─────────────────────────────────────────────────────────────
    builder.Services
        .AddIdentity<ApplicationUser, IdentityRole>(opts =>
        {
            opts.Password.RequiredLength = 8;
            opts.Password.RequireNonAlphanumeric = true;
            opts.Password.RequireDigit = true;
            opts.Password.RequireUppercase = true;
            opts.Lockout.MaxFailedAccessAttempts = 5;
            opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            opts.SignIn.RequireConfirmedEmail = false; // set true in prod if SMTP ready
            opts.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

    // ─── Authentication / Cookie ──────────────────────────────────────────────
    builder.Services.ConfigureApplicationCookie(opts =>
    {
        opts.LoginPath = "/Account/Login";
        opts.LogoutPath = "/Account/Logout";
        opts.AccessDeniedPath = "/Account/AccessDenied";
        opts.ExpireTimeSpan = TimeSpan.FromHours(8);
        opts.SlidingExpiration = true;
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        opts.Cookie.SameSite = SameSiteMode.Lax;
    });

    // ─── MVC + Razor runtime compilation (dev) ────────────────────────────────
    builder.Services.AddControllersWithViews(o => o.Filters.Add<NavSeenFilter>())
        .AddRazorRuntimeCompilation();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<NavBadgeService>();

    // ─── SMTP options ─────────────────────────────────────────────────────────
    var smtpOpts = new SmtpOptions();
    builder.Configuration.GetSection(SmtpOptions.Section).Bind(smtpOpts);
    builder.Services.AddSingleton(smtpOpts);

    // ─── Payment options ──────────────────────────────────────────────────────
    var mockPayOpts = new MockPaymentOptions();
    builder.Configuration.GetSection(MockPaymentOptions.Section).Bind(mockPayOpts);
    builder.Services.AddSingleton(mockPayOpts);

    // ─── Application services ─────────────────────────────────────────────────
    builder.Services.AddScoped<IAuditService, AuditService>();
    builder.Services.AddSingleton<EmailQueue>();
    builder.Services.AddHostedService<EmailSenderJob>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<IPdfService, PdfService>();
    builder.Services.AddScoped<IPaymentGateway, MockPaymentGateway>();
    builder.Services.AddScoped<IApplicationService, ApplicationService>();
    builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
    builder.Services.AddScoped<IAllocationService, AllocationService>();
    builder.Services.AddScoped<IPaymentService, PaymentService>();
    builder.Services.AddScoped<IDocumentService, DocumentService>();
    builder.Services.AddScoped<ITeacherService, TeacherService>();
    builder.Services.AddScoped<ICalendarService, CalendarService>();

    // UC14 – Question Papers
    builder.Services.AddScoped<IQuestionPaperService, QuestionPaperService>();
    builder.Services.AddHostedService<QuestionPaperExpiryJob>();

    // UC16 – Timetable
    builder.Services.AddScoped<ITimetableService, TimetableService>();

    //Event Management
    builder.Services.AddScoped<IEventManagementService, EventManagementService>();

    // Transport Module
    builder.Services.AddScoped<ITransportService, TransportService>();

    // Extracurricular Module
    builder.Services.AddScoped<IExtracurricularService, ExtracurricularService>();

    // Boarding & Meals Module (Increment 3)
    builder.Services.AddScoped<IStaffOnboardingService, StaffOnboardingService>();
    builder.Services.AddScoped<IBoardingService, BoardingService>();
    builder.Services.AddScoped<IMealPlanService, MealPlanService>();
    builder.Services.AddScoped<IMealLibraryService, MealLibraryService>();
    builder.Services.AddScoped<IDietaryService, DietaryService>();
    builder.Services.AddScoped<IInventoryService, InventoryService>();
    builder.Services.AddScoped<IMealOrderService, MealOrderService>();
    builder.Services.AddScoped<IKitchenScheduleService, KitchenScheduleService>();
    builder.Services.AddScoped<IMealAttendanceService, MealAttendanceService>();
    builder.Services.AddScoped<IMealFeedbackService, MealFeedbackService>();
    builder.Services.AddScoped<ILearnerAccountService, LearnerAccountService>();

    // ─── HTTPS / HSTS ─────────────────────────────────────────────────────────
    builder.Services.AddHsts(opts =>
    {
        opts.MaxAge = TimeSpan.FromDays(365);
        opts.IncludeSubDomains = true;
        opts.Preload = true;
    });

    // ─── Anti-forgery ─────────────────────────────────────────────────────────
    builder.Services.AddAntiforgery(opts =>
    {
        opts.HeaderName = "X-CSRF-TOKEN";
        opts.Cookie.Name = "__RequestVerificationToken";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

    // ─── File upload size limit (50 MB) ───────────────────────────────────────
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
    {
        opts.MultipartBodyLengthLimit = 50_000_000;
    });

    //Calendar Registar
    // Auto-apply migrations on startup
    

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();
    // ─────────────────────────────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
    }
    // Azure App Service (and most reverse proxies) terminate TLS at the edge and
    // forward requests as plain HTTP with X-Forwarded-* headers. Without this,
    // HttpContext.Request.IsHttps is false behind the proxy, which breaks anything
    // that requires a "secure" context — e.g. cookies with SecurePolicy = Always.
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                if (exceptionFeature?.Error is Exception ex)
                {
                    Log.Error(ex, "Unhandled exception on {Path}", exceptionFeature.Path);
                }

                context.Response.Redirect("/Home/Error");
            });
        });
        app.UseHsts();
    }
    else
    {
        app.UseDeveloperExceptionPage();
    }

    // en-ZA uses "," as the decimal separator, but HTML number inputs always post ".".
    // Use en-ZA with a "." separator so decimals (recipe quantities, prices) bind correctly
    // regardless of the server's regional settings.
    var appCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.GetCultureInfo("en-ZA").Clone();
    appCulture.NumberFormat.NumberDecimalSeparator = ".";
    appCulture.NumberFormat.CurrencyDecimalSeparator = ".";
    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(appCulture),
        SupportedCultures = new[] { appCulture },
        SupportedUICultures = new[] { appCulture }
    });

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseSerilogRequestLogging();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    // Ensure upload/proof directories exist before registering static file providers
    var uploadsPath = Path.Combine(app.Environment.WebRootPath, "uploads");
    var proofsPath = Path.Combine(app.Environment.WebRootPath, "proofs");
    Directory.CreateDirectory(uploadsPath);
    Directory.CreateDirectory(proofsPath);

    // Static file uploads — NOT served without auth in prod; use blob storage instead
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads"
    });

    app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    // ─── Seed database ────────────────────────────────────────────────────────
    await SeedData.SeedAsync(app.Services);

    Log.Information("Dlangezwa HS application starting…");
    Log.Information("Environment : {Env}", app.Environment.EnvironmentName);
    Log.Information("SMTP Host   : {Host}", builder.Configuration["Smtp:Host"] ?? "NOT SET");
    Log.Information("SMTP User   : {User}", string.IsNullOrEmpty(builder.Configuration["Smtp:UserName"]) ? "NOT SET" : "configured");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
