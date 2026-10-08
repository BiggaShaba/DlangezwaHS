using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// LEARNER ACCOUNTS — parent-created logins linked to a Learner record.
// Learners sign in with their Learner ID number (stored as the UserName).
// ─────────────────────────────────────────────────────────────────────────────

public interface ILearnerAccountService
{
    Task<Learner?> GetLearnerForUserAsync(string userId);
    Task<IdentityResult> CreateLoginAsync(Learner learner, string password, string? email);
    Task<IdentityResult> ResetPasswordAsync(Learner learner, string newPassword);
    Task SetAccessAsync(Learner learner, bool enabled);
    Task<bool> IsAccessEnabledAsync(Learner learner);
}

public class LearnerAccountService : ILearnerAccountService
{
    public const string RoleName = "Learner";

    // Identity requires a unique email; learners without one get an undeliverable
    // placeholder on the reserved ".invalid" TLD so nothing is ever sent to it.
    public const string PlaceholderEmailDomain = "learners.dlangezwa.invalid";

    public static bool IsPlaceholderEmail(string? email)
        => email is not null && email.EndsWith("@" + PlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public LearnerAccountService(ApplicationDbContext db, UserManager<ApplicationUser> um)
    {
        _db = db;
        _userManager = um;
    }

    public async Task<Learner?> GetLearnerForUserAsync(string userId)
        => string.IsNullOrEmpty(userId) ? null : await _db.Learners.FirstOrDefaultAsync(l => l.UserId == userId);

    public async Task<IdentityResult> CreateLoginAsync(Learner learner, string password, string? email)
    {
        if (learner.HasLogin)
            return IdentityResult.Failed(new IdentityError { Description = "This learner already has a login." });

        var user = new ApplicationUser
        {
            UserName       = learner.LearnerIdNumber.Trim(),
            Email          = string.IsNullOrWhiteSpace(email)
                                 ? $"{learner.LearnerIdNumber.Trim()}@{PlaceholderEmailDomain}"
                                 : email.Trim(),
            FirstName      = learner.FirstName,
            LastName       = learner.LastName,
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded) return result;

        var roleResult = await _userManager.AddToRoleAsync(user, RoleName);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return roleResult;
        }

        learner.UserId = user.Id;
        await _db.SaveChangesAsync();
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> ResetPasswordAsync(Learner learner, string newPassword)
    {
        var user = learner.HasLogin ? await _userManager.FindByIdAsync(learner.UserId) : null;
        if (user is null)
            return IdentityResult.Failed(new IdentityError { Description = "This learner does not have a login yet." });

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded) await _userManager.ResetAccessFailedCountAsync(user);
        return result;
    }

    // Disabling uses IsActive + a permanent lockout so existing sign-in checks reject it
    public async Task SetAccessAsync(Learner learner, bool enabled)
    {
        var user = learner.HasLogin ? await _userManager.FindByIdAsync(learner.UserId) : null;
        if (user is null) return;

        user.IsActive = enabled;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, enabled ? null : DateTimeOffset.MaxValue);
        // Sign the learner out of any open sessions
        await _userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<bool> IsAccessEnabledAsync(Learner learner)
    {
        var user = learner.HasLogin ? await _userManager.FindByIdAsync(learner.UserId) : null;
        return user is not null && user.IsActive;
    }
}
