using DlangezwaHS.Web.Data;
using DlangezwaHS.Web.Models.Domain;
using DlangezwaHS.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface IStaffOnboardingService
{
    // Housemaster
    Task<(Housemaster housemaster, string password)> RegisterHousemasterAsync(RegisterHousemasterViewModel vm);
    Task UpdateHousemasterAsync(int id, RegisterHousemasterViewModel vm);
    Task DeactivateHousemasterAsync(int id);
    Task<IList<HousemasterSummaryRow>> GetHousemastersAsync();
    Task<Housemaster?> GetHousemasterByUserIdAsync(string userId);

    // Kitchen Staff
    Task<(KitchenStaffMember staff, string password)> RegisterKitchenStaffAsync(RegisterKitchenStaffViewModel vm);
    Task UpdateKitchenStaffAsync(int id, RegisterKitchenStaffViewModel vm);
    Task DeactivateKitchenStaffAsync(int id);
    Task<IList<KitchenStaffSummaryRow>> GetKitchenStaffAsync();
    Task<KitchenStaffMember?> GetKitchenStaffByUserIdAsync(string userId);
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPLEMENTATION
// ─────────────────────────────────────────────────────────────────────────────

public class StaffOnboardingService : IStaffOnboardingService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;

    public StaffOnboardingService(ApplicationDbContext db, UserManager<ApplicationUser> um, IEmailService email)
    {
        _db = db;
        _userManager = um;
        _email = email;
    }

    private static string GeneratePassword(string firstName)
    {
        var rnd = new Random();
        var number = rnd.Next(1000, 9999);
        var name = firstName.Length >= 4 ? firstName[..4] : firstName;
        return $"{char.ToUpper(name[0])}{name[1..].ToLower()}{number}!";
    }

    // ── Housemaster ───────────────────────────────────────────────────────────

    public async Task<(Housemaster housemaster, string password)> RegisterHousemasterAsync(RegisterHousemasterViewModel vm)
    {
        var password = GeneratePassword(vm.FirstName);
        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Phone = vm.Phone,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Could not create housemaster account: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "Housemaster");

        var housemaster = new Housemaster
        {
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Email = vm.Email,
            Phone = vm.Phone,
            UserId = user.Id,
            IsActive = true
        };
        _db.Housemasters.Add(housemaster);
        await _db.SaveChangesAsync();

        await _email.SendHousemasterCredentialsAsync(vm.Email, housemaster.FullName, password);

        return (housemaster, password);
    }

    public async Task UpdateHousemasterAsync(int id, RegisterHousemasterViewModel vm)
    {
        var housemaster = await _db.Housemasters.FirstOrDefaultAsync(h => h.Id == id);
        if (housemaster is null) return;
        housemaster.FirstName = vm.FirstName;
        housemaster.LastName = vm.LastName;
        housemaster.Phone = vm.Phone;
        await _db.SaveChangesAsync();
    }

    public async Task DeactivateHousemasterAsync(int id)
    {
        var housemaster = await _db.Housemasters.FirstOrDefaultAsync(h => h.Id == id);
        if (housemaster is null) return;
        housemaster.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<IList<HousemasterSummaryRow>> GetHousemastersAsync()
    {
        return await _db.Housemasters
            .OrderBy(h => h.LastName)
            .Select(h => new HousemasterSummaryRow
            {
                Id = h.Id,
                FullName = h.FullName,
                Email = h.Email ?? "",
                Phone = h.Phone ?? "",
                HasLogin = h.UserId != null,
                IsActive = h.IsActive
            })
            .ToListAsync();
    }

    public async Task<Housemaster?> GetHousemasterByUserIdAsync(string userId)
        => await _db.Housemasters.FirstOrDefaultAsync(h => h.UserId == userId);

    // ── Kitchen Staff ─────────────────────────────────────────────────────────

    public async Task<(KitchenStaffMember staff, string password)> RegisterKitchenStaffAsync(RegisterKitchenStaffViewModel vm)
    {
        var password = GeneratePassword(vm.FirstName);
        var user = new ApplicationUser
        {
            UserName = vm.Email,
            Email = vm.Email,
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Phone = vm.Phone,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Could not create kitchen staff account: " +
                string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, "KitchenStaff");

        var staff = new KitchenStaffMember
        {
            FirstName = vm.FirstName,
            LastName = vm.LastName,
            Email = vm.Email,
            Phone = vm.Phone,
            UserId = user.Id,
            IsActive = true
        };
        _db.KitchenStaffMembers.Add(staff);
        await _db.SaveChangesAsync();

        await _email.SendKitchenStaffCredentialsAsync(vm.Email, staff.FullName, password);

        return (staff, password);
    }

    public async Task UpdateKitchenStaffAsync(int id, RegisterKitchenStaffViewModel vm)
    {
        var staff = await _db.KitchenStaffMembers.FirstOrDefaultAsync(k => k.Id == id);
        if (staff is null) return;
        staff.FirstName = vm.FirstName;
        staff.LastName = vm.LastName;
        staff.Phone = vm.Phone;
        await _db.SaveChangesAsync();
    }

    public async Task DeactivateKitchenStaffAsync(int id)
    {
        var staff = await _db.KitchenStaffMembers.FirstOrDefaultAsync(k => k.Id == id);
        if (staff is null) return;
        staff.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<IList<KitchenStaffSummaryRow>> GetKitchenStaffAsync()
    {
        return await _db.KitchenStaffMembers
            .OrderBy(k => k.LastName)
            .Select(k => new KitchenStaffSummaryRow
            {
                Id = k.Id,
                FullName = k.FullName,
                Email = k.Email ?? "",
                Phone = k.Phone ?? "",
                HasLogin = k.UserId != null,
                IsActive = k.IsActive
            })
            .ToListAsync();
    }

    public async Task<KitchenStaffMember?> GetKitchenStaffByUserIdAsync(string userId)
        => await _db.KitchenStaffMembers.FirstOrDefaultAsync(k => k.UserId == userId);
}
