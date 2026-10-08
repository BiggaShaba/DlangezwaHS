using DlangezwaHS.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DlangezwaHS.Web.Data;

public static class SeedData
{
    // ── Demo credentials (shown in README) ────────────────────────────────────
    public const string AdminEmail     = "admin@dlangezwa.edu.za";
    public const string AdminPassword  = "Admin@Dlangezwa2024!";
    public const string ParentEmail    = "demo.parent@example.com";
    public const string ParentPassword = "Parent@Demo2024!";
    public const string TeacherEmail   = "demo.teacher@dlangezwa.edu.za";
    public const string TeacherPassword = "Teacher@Demo2024!";
    public const string HousemasterEmail    = "demo.housemaster@dlangezwa.edu.za";
    public const string HousemasterPassword = "Housemaster@Demo2024!";
    public const string KitchenPassword     = "Kitchen@Demo2024!";   // all demo kitchen staff

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger      = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        try
        {
            // ── 1. Migrations ─────────────────────────────────────────────────
            logger.LogInformation("Applying database migrations...");
              await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully.");

            // ── 2. Roles ──────────────────────────────────────────────────────
            foreach (var role in new[] { "Admin", "Parent", "Teacher", "Housemaster", "KitchenStaff", "Learner" })
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole(role));
                    if (!result.Succeeded)
                        logger.LogError("Failed to create role {Role}: {Errors}", role,
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

            // ── 3. Admin user ─────────────────────────────────────────────────
            if (await userManager.FindByEmailAsync(AdminEmail) == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = AdminEmail, Email = AdminEmail,
                    FirstName = "System", LastName = "Administrator",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(admin, AdminPassword);
                if (result.Succeeded) await userManager.AddToRoleAsync(admin, "Admin");
            }

            // ── 4. Demo parent user ───────────────────────────────────────────
            if (await userManager.FindByEmailAsync(ParentEmail) == null)
            {
                var parent = new ApplicationUser
                {
                    UserName = ParentEmail, Email = ParentEmail,
                    FirstName = "Demo", LastName = "Parent",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(parent, ParentPassword);
                if (result.Succeeded) await userManager.AddToRoleAsync(parent, "Parent");
            }

            // ── 5. Grades 8-12 ────────────────────────────────────────────────
            if (!await db.Grades.AnyAsync())
            {
                var grades = Enumerable.Range(8, 5)
                    .Select(n => new Grade { Name = $"Grade {n}", Level = n })
                    .ToList();
                db.Grades.AddRange(grades);
                await db.SaveChangesAsync();

                var sections = new[] { "A", "B", "C", "D" };
                foreach (var grade in grades)
                    foreach (var sec in sections)
                        db.Classes.Add(new Class { GradeId = grade.Id, Section = sec, Capacity = 40 });
                await db.SaveChangesAsync();
            }

            // ── 6. Core subjects ──────────────────────────────────────────────
            if (!await db.Subjects.AnyAsync())
            {
                db.Subjects.AddRange(
                    new Subject { Name = "Mathematics",      Code = "MATH" },
                    new Subject { Name = "English",          Code = "ENG"  },
                    new Subject { Name = "Life Sciences",    Code = "LSCI" },
                    new Subject { Name = "Geography",        Code = "GEO"  },
                    new Subject { Name = "History",          Code = "HIST" },
                    new Subject { Name = "Accounting",       Code = "ACC"  },
                    new Subject { Name = "Physical Science", Code = "PHYS" },
                    new Subject { Name = "Life Orientation", Code = "LO"   }
                );
                await db.SaveChangesAsync();
            }

            // ── 7. Fee types ──────────────────────────────────────────────────
            if (!await db.FeeTypes.AnyAsync())
            {
                db.FeeTypes.AddRange(
                    new FeeType { Name = "Registration Fee",  Amount = 2567, Description = "Annual registration fee"           },
                    new FeeType { Name = "Accommodation Fee", Amount = 5439, Description = "Annual boarding accommodation fee" }
                );
                await db.SaveChangesAsync();
            }

            // ── 8. Email templates ────────────────────────────────────────────
            if (!await db.EmailTemplates.AnyAsync())
            {
                db.EmailTemplates.AddRange(
                    new EmailTemplate
                    {
                        TemplateKey = "ApplicationReceived",
                        Subject     = "Application Received – {{LearnerName}}",
                        Body        = @"<p>Dear {{ParentName}},</p>
<p>Thank you for submitting an application for <strong>{{LearnerName}}</strong> at Dlangezwa High School.</p>
<p>Your application reference is <strong>#{{ApplicationId}}</strong> and its current status is <strong>Pending Review</strong>.</p>
<p>You will be notified by email once a decision has been made.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "ApplicationApproved",
                        Subject     = "Application Approved – {{LearnerName}}",
                        Body        = @"<p>Dear {{ParentName}},</p>
<p>We are pleased to inform you that the application for <strong>{{LearnerName}}</strong> has been <strong>approved</strong>.</p>
<p>The school administration will be in touch regarding enrolment details and next steps.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "ApplicationRejected",
                        Subject     = "Application Outcome – {{LearnerName}}",
                        Body        = @"<p>Dear {{ParentName}},</p>
<p>After careful review, we regret to inform you that the application for <strong>{{LearnerName}}</strong> has been <strong>unsuccessful</strong>.</p>
<p><strong>Reason:</strong> {{RejectionReason}}</p>
<p>If you have any questions, please contact the admissions office.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "PaymentConfirmation",
                        Subject     = "Payment Confirmation – {{LearnerName}}",
                        Body        = @"<p>Dear {{ParentName}},</p>
<p>Your payment of <strong>R{{Amount}}</strong> for {{PaymentType}} has been successfully recorded.</p>
<p>Reference: <strong>{{PaymentRef}}</strong></p>
<p>Please find your Proof of Registration attached to this email.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "RegistrationProof",
                        Subject     = "Proof of Registration – {{LearnerName}}",
                        Body        = @"<p>Dear {{ParentName}},</p>
<p>Please find attached the Proof of Registration for <strong>{{LearnerName}}</strong>.</p>
<p>Class: <strong>{{ClassName}}</strong></p>
<p>Subjects: <strong>{{Subjects}}</strong></p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "TeacherWelcome",
                        Subject     = "Your Teacher Account – Dlangezwa High School",
                        Body        = @"<p>Dear {{TeacherName}},</p>
<p>Your teacher account has been created on the Dlangezwa High School portal.</p>
<p><strong>Email:</strong> {{Email}}<br/>
<strong>Temporary Password:</strong> <code>{{Password}}</code></p>
<p>Please log in and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "HousemasterWelcome",
                        Subject     = "Your Housemaster Account – Dlangezwa High School",
                        Body        = @"<p>Dear {{HousemasterName}},</p>
<p>Your housemaster account has been created on the Dlangezwa High School boarding portal.</p>
<p><strong>Email:</strong> {{Email}}<br/>
<strong>Temporary Password:</strong> <code>{{Password}}</code></p>
<p>Please log in and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    },
                    new EmailTemplate
                    {
                        TemplateKey = "KitchenStaffWelcome",
                        Subject     = "Your Kitchen Staff Account – Dlangezwa High School",
                        Body        = @"<p>Dear {{StaffName}},</p>
<p>Your kitchen staff account has been created on the Dlangezwa High School boarding portal.</p>
<p><strong>Email:</strong> {{Email}}<br/>
<strong>Temporary Password:</strong> <code>{{Password}}</code></p>
<p>Please log in and change your password on first use.</p>
<p>Regards,<br/>Dlangezwa High School Administration</p>"
                    }
                );
                await db.SaveChangesAsync();
            }

            // ── 9. Demo Teacher account ──────────────────────────────────────
            // Guard on the Identity USER — not the Teacher table — so a partial
            // previous run does not prevent re-seeding the full record.
            var existingTeacherUser = await userManager.FindByEmailAsync(TeacherEmail);

            if (existingTeacherUser == null)
            {
                logger.LogInformation("Seeding demo teacher Identity user...");

                // Step A — create the ASP.NET Core Identity user
                var teacherUser = new ApplicationUser
                {
                    UserName       = TeacherEmail,
                    Email          = TeacherEmail,
                    FirstName      = "Demo",
                    LastName       = "Teacher",
                    Phone          = "0821234567",
                    EmailConfirmed = true,
                    IsActive       = true
                };

                var createResult = await userManager.CreateAsync(teacherUser, TeacherPassword);
                if (!createResult.Succeeded)
                {
                    logger.LogError("Could not create demo teacher user: {Errors}",
                        string.Join(" | ", createResult.Errors.Select(e => e.Description)));
                }
                else
                {
                    await userManager.AddToRoleAsync(teacherUser, "Teacher");
                    logger.LogInformation("Demo teacher Identity user created: {Id}", teacherUser.Id);

                    // Step B — remove any orphaned Teacher record for this email
                    var orphan = await db.Teachers.FirstOrDefaultAsync(t => t.Email == TeacherEmail);
                    if (orphan is not null)
                    {
                        db.Teachers.Remove(orphan);
                        await db.SaveChangesAsync();
                    }

                    // Step C — create the Teacher profile record linked to the user
                    var teacher = new Teacher
                    {
                        FirstName = "Demo",
                        LastName  = "Teacher",
                        Email     = TeacherEmail,
                        Phone     = "0821234567",
                        UserId    = teacherUser.Id,
                        IsActive  = true
                    };
                    db.Teachers.Add(teacher);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Demo Teacher profile created: Id={Id}, UserId={Uid}",
                        teacher.Id, teacherUser.Id);

                    // Step D — assign Grade 8A – Mathematics and Grade 8A – English
                    var grade8      = await db.Grades.FirstOrDefaultAsync(g => g.Level == 8);
                    var class8A     = grade8 is not null
                                    ? await db.Classes.FirstOrDefaultAsync(c => c.GradeId == grade8.Id && c.Section == "A")
                                    : null;
                    var mathSubject = await db.Subjects.FirstOrDefaultAsync(s => s.Code == "MATH");
                    var engSubject  = await db.Subjects.FirstOrDefaultAsync(s => s.Code == "ENG");

                    if (class8A is not null && mathSubject is not null)
                        db.TeacherClassSubjects.Add(new TeacherClassSubject
                            { TeacherId = teacher.Id, ClassId = class8A.Id, SubjectId = mathSubject.Id });

                    if (class8A is not null && engSubject is not null)
                        db.TeacherClassSubjects.Add(new TeacherClassSubject
                            { TeacherId = teacher.Id, ClassId = class8A.Id, SubjectId = engSubject.Id });

                    await db.SaveChangesAsync();
                    logger.LogInformation("Demo teacher seeded successfully: {Email}", TeacherEmail);
                }
            }
            else
            {
                // User exists — make sure the Teacher profile and role are in place
                if (!await userManager.IsInRoleAsync(existingTeacherUser, "Teacher"))
                    await userManager.AddToRoleAsync(existingTeacherUser, "Teacher");

                var profile = await db.Teachers.FirstOrDefaultAsync(t => t.Email == TeacherEmail);
                if (profile is null)
                {
                    db.Teachers.Add(new Teacher
                    {
                        FirstName = "Demo", LastName = "Teacher",
                        Email     = TeacherEmail, Phone = "0821234567",
                        UserId    = existingTeacherUser.Id, IsActive = true
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Repaired missing Teacher profile for {Email}", TeacherEmail);
                }
                else if (profile.UserId != existingTeacherUser.Id)
                {
                    profile.UserId = existingTeacherUser.Id;
                    await db.SaveChangesAsync();
                    logger.LogInformation("Repaired Teacher UserId link for {Email}", TeacherEmail);
                }
            }

            // ── 10. Boarding settings (Increment 3) ────────────────────────────
            if (!await db.BoardingSettings.AnyAsync())
            {
                db.BoardingSettings.Add(new BoardingSettings());
                await db.SaveChangesAsync();
            }

            // ── 11. Boarding & kitchen demo data ─────────────────────────────
            await SeedBoardingStaffAsync(db, userManager, logger);
            await SeedKitchenLibraryAsync(db, logger);

            // ── 12. Demo kitchen activity (orders, collections, usage, feedback) ──
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            if (config.GetValue<bool>("DemoData:Enabled"))
            {
                var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
                await DemoDataSeeder.SeedAsync(db, env.ContentRootPath, logger);
            }

            logger.LogInformation("Database seeded successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during database migration/seeding.");
            throw;
        }
    }

    // Demo housemaster (approves dietary profiles) + kitchen staff grouped into teams
    // Each demo record is keyed on its email / team name, so this is safe to run on every start
    private static async Task SeedBoardingStaffAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        if (!await db.Housemasters.AnyAsync(h => h.Email == HousemasterEmail))
        {
            var user = await EnsureUserAsync(userManager, HousemasterEmail, HousemasterPassword, "Demo", "Housemaster", "Housemaster", logger);
            if (user is not null)
            {
                db.Housemasters.Add(new Housemaster { FirstName = "Demo", LastName = "Housemaster", Email = HousemasterEmail, UserId = user.Id });
                await db.SaveChangesAsync();
            }
        }

        var teams = new (string Team, (string First, string Last)[] Staff)[]
        {
            ("Morning Crew",   new[] { ("Thandiwe", "Mthembu"), ("Sipho", "Ngcobo"), ("Nomvula", "Zulu") }),
            ("Afternoon Crew", new[] { ("Bongani", "Khumalo"), ("Lindiwe", "Dlamini"), ("Mandla", "Shezi") })
        };

        foreach (var (teamName, staff) in teams)
        {
            var team = await db.KitchenTeams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Name == teamName);
            var teamIsNew = team is null;
            if (team is null)
            {
                team = new KitchenTeam { Name = teamName };
                db.KitchenTeams.Add(team);
            }

            foreach (var (first, last) in staff)
            {
                var email = $"{first.ToLower()}.{last.ToLower()}@dlangezwa.edu.za";
                var member = await db.KitchenStaffMembers.FirstOrDefaultAsync(k => k.Email == email);
                var memberIsNew = member is null;
                if (member is null)
                {
                    var user = await EnsureUserAsync(userManager, email, KitchenPassword, first, last, "KitchenStaff", logger);
                    if (user is null) continue;
                    member = new KitchenStaffMember { FirstName = first, LastName = last, Email = email, UserId = user.Id };
                    db.KitchenStaffMembers.Add(member);
                    await db.SaveChangesAsync();
                }
                // Only link on first creation so an admin's later team edits are respected
                if ((teamIsNew || memberIsNew) && !team.Members.Any(m => m.KitchenStaffMemberId == member.Id))
                    team.Members.Add(new KitchenTeamMember { KitchenStaffMemberId = member.Id });
            }
            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser?> EnsureUserAsync(UserManager<ApplicationUser> userManager,
        string email, string password, string first, string last, string role, ILogger logger)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FirstName = first, LastName = last, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Could not create demo user {Email}: {Errors}", email, string.Join(", ", result.Errors.Select(e => e.Description)));
                return null;
            }
        }
        if (!await userManager.IsInRoleAsync(user, role)) await userManager.AddToRoleAsync(user, role);
        return user;
    }

    // Starter inventory + a library of meals with per-serving ingredient quantities
    private static async Task SeedKitchenLibraryAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.Meals.AnyAsync()) return;

        var ingredientSeed = new (string Name, StockUnit Unit, decimal Stock, decimal Min, decimal Cost)[]
        {
            ("Maize meal", StockUnit.Kg, 100, 25, 12.00m),   ("Oats", StockUnit.Kg, 30, 8, 28.00m),
            ("Rice", StockUnit.Kg, 80, 20, 22.00m),          ("Samp", StockUnit.Kg, 40, 10, 18.00m),
            ("Sugar beans", StockUnit.Kg, 30, 8, 32.00m),    ("Pasta", StockUnit.Kg, 30, 8, 25.00m),
            ("Bread", StockUnit.Units, 60, 20, 17.00m),      ("Eggs", StockUnit.Units, 360, 120, 2.50m),
            ("Milk", StockUnit.Litres, 60, 20, 18.00m),      ("Sugar", StockUnit.Kg, 25, 5, 20.00m),
            ("Beef stewing", StockUnit.Kg, 40, 10, 110.00m), ("Chicken pieces", StockUnit.Kg, 50, 15, 65.00m),
            ("Beef mince", StockUnit.Kg, 25, 8, 95.00m),     ("Hake", StockUnit.Kg, 20, 5, 90.00m),
            ("Onions", StockUnit.Kg, 30, 10, 15.00m),        ("Tomatoes", StockUnit.Kg, 25, 8, 20.00m),
            ("Potatoes", StockUnit.Kg, 60, 20, 12.00m),      ("Carrots", StockUnit.Kg, 25, 8, 14.00m),
            ("Cabbage", StockUnit.Units, 20, 6, 20.00m),     ("Spinach", StockUnit.Kg, 10, 3, 30.00m),
            ("Cooking oil", StockUnit.Litres, 20, 5, 35.00m),("Jam", StockUnit.Kg, 8, 2, 40.00m),
            ("Baked beans", StockUnit.Units, 48, 12, 14.00m),("Butternut", StockUnit.Kg, 20, 5, 16.00m),
        };

        var existing = await db.Ingredients.ToDictionaryAsync(i => i.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var s in ingredientSeed.Where(s => !existing.ContainsKey(s.Name)))
        {
            var ing = new Ingredient { Name = s.Name, Unit = s.Unit, CurrentStock = s.Stock, MinimumStock = s.Min, UnitCost = s.Cost };
            db.Ingredients.Add(ing);
            existing[s.Name] = ing;
        }
        await db.SaveChangesAsync();

        Meal M(string name, MealType type, string description, params (string Ingredient, decimal Qty)[] recipe)
        {
            var meal = new Meal { Name = name, MealType = type, Description = description };
            foreach (var (ingredient, qty) in recipe)
                meal.Ingredients.Add(new MealIngredient { IngredientId = existing[ingredient].Id, QuantityPerServing = qty });
            return meal;
        }

        db.Meals.AddRange(
            // Breakfast
            M("Maize meal porridge", MealType.Breakfast, "Soft maize meal porridge served with warm milk and sugar.",
                ("Maize meal", 0.08m), ("Milk", 0.2m), ("Sugar", 0.02m)),
            M("Oats porridge", MealType.Breakfast, "Creamy oats cooked with milk, lightly sweetened.",
                ("Oats", 0.06m), ("Milk", 0.2m), ("Sugar", 0.02m)),
            M("Scrambled eggs & toast", MealType.Breakfast, "Two scrambled eggs with buttered toast.",
                ("Eggs", 2m), ("Bread", 0.15m), ("Cooking oil", 0.01m)),
            M("Toast with jam", MealType.Breakfast, "Toast with fruit jam — egg- and milk-free option.",
                ("Bread", 0.15m), ("Jam", 0.03m)),
            // Lunch
            M("Pap & beef stew", MealType.Lunch, "Stiff pap with slow-cooked beef and vegetable stew.",
                ("Maize meal", 0.12m), ("Beef stewing", 0.15m), ("Onions", 0.03m), ("Tomatoes", 0.04m), ("Carrots", 0.04m), ("Cooking oil", 0.01m)),
            M("Rice & chicken curry", MealType.Lunch, "Mild chicken and potato curry on white rice.",
                ("Rice", 0.12m), ("Chicken pieces", 0.2m), ("Onions", 0.03m), ("Tomatoes", 0.04m), ("Potatoes", 0.05m), ("Cooking oil", 0.01m)),
            M("Samp & beans", MealType.Lunch, "Traditional samp and sugar beans — vegetarian.",
                ("Samp", 0.1m), ("Sugar beans", 0.06m), ("Onions", 0.02m), ("Cooking oil", 0.01m)),
            M("Vegetable soup & bread", MealType.Lunch, "Hearty vegetable soup with fresh bread.",
                ("Potatoes", 0.08m), ("Carrots", 0.05m), ("Cabbage", 0.05m), ("Onions", 0.02m), ("Bread", 0.15m)),
            // Dinner
            M("Spaghetti bolognese", MealType.Dinner, "Spaghetti with beef mince and tomato sauce.",
                ("Pasta", 0.1m), ("Beef mince", 0.12m), ("Tomatoes", 0.05m), ("Onions", 0.03m), ("Cooking oil", 0.01m)),
            M("Hake, potatoes & spinach", MealType.Dinner, "Pan-fried hake with boiled potatoes and spinach.",
                ("Hake", 0.15m), ("Potatoes", 0.15m), ("Spinach", 0.05m), ("Cooking oil", 0.02m)),
            M("Chicken, rice & butternut", MealType.Dinner, "Roast chicken pieces with rice and butternut.",
                ("Chicken pieces", 0.2m), ("Rice", 0.1m), ("Butternut", 0.12m), ("Cooking oil", 0.01m)),
            M("Pap, chakalaka & beans", MealType.Dinner, "Pap with spicy chakalaka relish and baked beans — vegetarian.",
                ("Maize meal", 0.12m), ("Baked beans", 0.25m), ("Onions", 0.03m), ("Tomatoes", 0.03m), ("Carrots", 0.03m), ("Cooking oil", 0.01m))
        );
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded starter ingredients and meal library.");
    }
}
