using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DlangezwaHS.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser>  _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController>    _logger;

    public AccountController(UserManager<ApplicationUser> um, SignInManager<ApplicationUser> sm, ILogger<AccountController> logger)
    {
        _userManager  = um;
        _signInManager = sm;
        _logger       = logger;
    }

    // ── Register ──────────────────────────────────────────────────────────────

    [HttpGet] public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = new ApplicationUser
        {
            UserName       = vm.Email,
            Email          = vm.Email,
            FirstName      = vm.FirstName,
            LastName       = vm.LastName,
            Phone          = vm.Phone,
            EmailConfirmed = true  // Skip email verification for demo
        };

        var result = await _userManager.CreateAsync(user, vm.Password);
        if (!result.Succeeded)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError("", err.Description);
            return View(vm);
        }

        await _userManager.AddToRoleAsync(user, "Parent");
        await _signInManager.SignInAsync(user, isPersistent: false);

        _logger.LogInformation("New parent registered: {Email}", vm.Email);
        return RedirectToAction("Index", "Parent");
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpGet] public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // Parents/staff sign in with email; learners with their Learner ID (their UserName)
        var login = vm.Email.Trim();
        var user  = await _userManager.FindByEmailAsync(login) ?? await _userManager.FindByNameAsync(login);
        if (user is null)
        {
            ModelState.AddModelError("", "Invalid email/Learner ID or password.");
            return View(vm);
        }

        var result = await _signInManager.PasswordSignInAsync(user, vm.Password, vm.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User logged in: {Login}", login);
            var roles = await _userManager.GetRolesAsync(user);
            // Only follow the return URL when it belongs to this user's area — a URL left over
            // from another role (e.g. /Parent after a parent signed out) would show Access Denied.
            if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl) && ReturnUrlFitsRoles(vm.ReturnUrl, roles))
                return Redirect(vm.ReturnUrl);
            var (action, controller) = HomeFor(roles);
            return RedirectToAction(action, controller);
        }

        if (result.IsLockedOut)
        {
            // Parents disable a learner's login with a permanent lockout
            ModelState.AddModelError("", user.IsActive
                ? "Account locked. Try again in 15 minutes."
                : "This account has been disabled. Please speak to your parent.");
            return View(vm);
        }

        ModelState.AddModelError("", "Invalid email/Learner ID or password.");
        return View(vm);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpGet] public IActionResult AccessDenied() => View();

    // ── Role landing pages ────────────────────────────────────────────────────

    public static (string Action, string Controller) HomeFor(ICollection<string> roles) =>
        roles.Contains("Admin")        ? ("Dashboard", "Admin")
      : roles.Contains("Teacher")      ? ("Dashboard", "Teacher")
      : roles.Contains("Housemaster")  ? ("Dashboard", "Boarding")
      : roles.Contains("KitchenStaff") ? ("Dashboard", "KitchenStaff")
      : roles.Contains("Learner")      ? ("Index", "Learner")
      :                                  ("Index", "Parent");

    // Areas locked to a single role; anything else (Calendar, Transport, …) is left to [Authorize]
    private static readonly Dictionary<string, string> RoleAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Admin"] = "Admin", ["Teacher"] = "Teacher", ["Boarding"] = "Housemaster",
        ["KitchenStaff"] = "KitchenStaff", ["Learner"] = "Learner", ["Parent"] = "Parent"
    };

    private static bool ReturnUrlFitsRoles(string returnUrl, ICollection<string> roles)
    {
        var firstSegment = returnUrl.TrimStart('/').Split('/', '?', '#')[0];
        return !RoleAreas.TryGetValue(firstSegment, out var role) || roles.Contains(role);
    }
}
