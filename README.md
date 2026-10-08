# Dlangezwa High School — Administration & Boarding System

> ASP.NET Core MVC (.NET 8) · Entity Framework Core · Azure App Service + Azure SQL

A secure, production-ready school management web application covering the full learner onboarding workflow: **Apply → Approve → Enrol → Assign Subjects → Allocate Room & Bed → Pay → Proof of Registration**.

---

## Table of Contents

1. [Quick Start (Local)](#quick-start-local)
2. [Demo Credentials](#demo-credentials)
3. [Demo Scenario (Marking Script)](#demo-scenario-marking-script)
4. [Project Structure](#project-structure)
5. [Architecture & Data Model](#architecture--data-model)
6. [Configuration Reference](#configuration-reference)
7. [Deploy to Azure](#deploy-to-azure)
8. [Payment Gateway](#payment-gateway)
9. [Running Unit Tests](#running-unit-tests)
10. [ERD Diagram](#erd-diagram)

---

## Quick Start (Local)

### Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0+ |
| SQL Server / LocalDB | 2019+ |
| Node.js (optional, for CSS tooling) | — |

### 1 — Clone & restore

```bash
git clone https://github.com/<your-org>/dlangezwa-hs.git
cd dlangezwa-hs
dotnet restore src/DlangezwaHS.Web/DlangezwaHS.Web.csproj
```

### 2 — Configure appsettings

Edit `src/DlangezwaHS.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=DlangezwaHS_Dev;Trusted_Connection=True;"
  },
  "Smtp": {
    "Host":     "smtp.mailtrap.io",
    "Port":     587,
    "UserName": "YOUR_MAILTRAP_USER",
    "Password": "YOUR_MAILTRAP_PASS"
  }
}
```

> **Tip:** Get a free Mailtrap inbox at https://mailtrap.io to capture all outbound emails during development.

### 3 — Apply migrations & seed

```bash
cd src/DlangezwaHS.Web
dotnet ef database update
# The app also auto-runs SeedData on first startup
```

If EF tools aren't installed:

```bash
dotnet tool install --global dotnet-ef
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 4 — Run

```bash
dotnet run --project src/DlangezwaHS.Web
```

Browse to: **https://localhost:7001**

---

## Demo Credentials

| Role   | Email                        | Password              |
|--------|------------------------------|-----------------------|
| Admin  | `admin@dlangezwa.edu.za`     | `Admin@Dlangezwa2024!` |
| Parent | `demo.parent@example.com`    | `Parent@Demo2024!`    |

> These are created automatically by `SeedData.SeedAsync()` on first run.

---

## Demo Scenario (Marking Script)

Follow this single path to demonstrate the complete workflow to a lecturer:

### Step 1 — Admin is already seeded
Log in as **Admin**. The dashboard shows 0 pending applications.

### Step 2 — Parent registers and applies

1. Open an incognito / second browser window.
2. Go to `/Account/Register` → register with any email.
3. Log in as the new parent.
4. Click **New Application** → fill in learner details, upload three documents (any PDF/image).
5. Submit → you see status **Pending**.

### Step 3 — Admin reviews and approves

1. Switch back to the Admin browser.
2. Dashboard shows **1 Pending Application**.
3. Go to **Applications** → click **Review**.
4. Inspect uploaded documents and learner details.
5. Click **Approve** → confirm dialog → email sent to parent.

### Step 4 — Admin enrols into Grade 8A with subjects

1. On the Application Detail page, click **Proceed to Enrolment**.
2. Select **Grade 8A** from the dropdown.
3. Tick **Mathematics** and **English**.
4. Click **Confirm Enrolment**.

### Step 5 — Admin adds Room R1 with 4 beds and allocates Bed B1

1. Navigate to **Rooms → Add Room**.
2. Name: `R1`, Capacity: `4`, Auto-create beds: `4` → Save.
3. Go back to the Enrollment Detail for this learner.
4. Click **Allocate Bed** → select Room R1 → Bed B1 → Allocate.
5. Room card now shows B1 as Occupied.

### Step 6 — Record payments

From the Enrollment Detail page:

1. Click **Record Payment**.
2. Type: `Registration`, Amount: `2567`, Bank Ref: `FNB-REG-001` → Save.
3. Repeat for Accommodation: Amount: `5439`, Ref: `FNB-ACC-001`.

### Step 7 — Generate Proof of Registration

1. On Enrollment Detail, click **Generate & Email**.
2. PDF downloads automatically and is emailed to the parent.
3. Parent dashboard now shows the proof with a **Download PDF** button.

### Step 8 — Verify in DB (Lecturer check)

```sql
SELECT * FROM Enrollments;
SELECT * FROM Payments;
SELECT * FROM RegistrationProofs;
SELECT * FROM RoomAllocations;
SELECT * FROM AuditLogs ORDER BY Timestamp DESC;
```

---

## Project Structure

```
DlangezwaHS/
├── src/
│   └── DlangezwaHS.Web/
│       ├── Controllers/          ← AccountController, AdminController, ParentController, HomeController
│       ├── Data/                 ← ApplicationDbContext, SeedData
│       ├── Migrations/           ← EF Core migrations
│       ├── Models/Domain/        ← All domain entities
│       ├── Services/             ← Business logic (Application, Enrollment, Allocation, Payment, PDF, Email)
│       ├── ViewModels/           ← Request/response view models
│       ├── Views/                ← Razor views (Admin, Parent, Account, Home, Shared)
│       ├── wwwroot/              ← CSS, JS, uploaded documents, generated PDFs
│       ├── Program.cs            ← DI composition root
│       ├── appsettings.json
│       └── DlangezwaHS.Web.csproj
├── tests/
│   └── DlangezwaHS.Tests/        ← xUnit tests (ApplicationService, EnrollmentService, AllocationService, PaymentService, full workflow)
├── .github/workflows/ci-cd.yml   ← GitHub Actions build + Azure deploy
├── azure-infra.bicep              ← Bicep IaC (App Service + SQL + Key Vault)
└── README.md
```

---

## Architecture & Data Model

### Key Entities

```
ApplicationUser (Identity)
  └─< Learner ──< LearnerApplication ──> Enrollment ──> Class ──> Grade
                                                    └──< EnrollmentSubject >── Subject
  └─< Payment
  └─< RoomAllocation ──> Room ──< Bed
  └─< RegistrationProof

EmailTemplate
AuditLog
FeeType
Teacher ──< TeacherSubject >── Subject
```

### Service Layer

| Service | Responsibility |
|---------|---------------|
| `ApplicationService` | Submit, approve, reject applications; trigger email notifications |
| `EnrollmentService` | Enrol learner into class + subjects; update application status |
| `AllocationService` | Allocate/deallocate room beds; manage bed availability |
| `PaymentService` | Manual & online payments; PDF proof generation & email |
| `EmailService` | Template-based email via MailKit/SMTP |
| `PdfService` | QuestPDF-generated registration proof & payment receipt |
| `DocumentService` | File upload validation and storage |
| `AuditService` | Immutable audit trail for all significant actions |

---

## Configuration Reference

### appsettings.json sections

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "<SQL Server connection string>"
  },

  "KeyVaultUri": "<optional Azure Key Vault URI>",

  "Smtp": {
    "Host":        "smtp.mailtrap.io",
    "Port":        587,
    "UserName":    "",
    "Password":    "",
    "UseSsl":      false,
    "FromAddress": "noreply@dlangezwa.edu.za",
    "FromName":    "Dlangezwa High School"
  },

  "Payment": {
    "Mock": {
      "SandboxMode": true
    }
  }
}
```

### Azure App Service Application Settings

Set these in the Azure Portal under **App Service → Configuration → Application Settings** (or store secrets in Key Vault):

| Key | Description |
|-----|-------------|
| `ConnectionStrings__DefaultConnection` | Azure SQL connection string |
| `KeyVaultUri` | `https://<vault-name>.vault.azure.net/` |
| `Smtp__Host` | SMTP host (or SendGrid SMTP relay) |
| `Smtp__UserName` | SMTP username |
| `Smtp__Password` | SMTP password (store in Key Vault) |

---

## Deploy to Azure

### Option A — Bicep (recommended)

```bash
# 1. Login
az login

# 2. Create resource group
az group create --name dlangezwa-rg --location southafricanorth

# 3. Deploy infrastructure
az deployment group create \
  --resource-group dlangezwa-rg \
  --template-file azure-infra.bicep \
  --parameters appName=dlangezwa-hs sqlAdminPassword='<STRONG_PASS>'

# 4. Note the outputs: webAppUrl, keyVaultUri, sqlServerFqdn

# 5. Publish app
dotnet publish src/DlangezwaHS.Web -c Release -o ./publish
az webapp deploy --resource-group dlangezwa-rg --name dlangezwa-hs \
  --src-path ./publish --type zip
```

### Option B — GitHub Actions (CI/CD)

1. Fork this repo.
2. In Azure Portal, download the **Publish Profile** from your App Service.
3. In GitHub repo **Settings → Secrets**, add: `AZURE_WEBAPP_PUBLISH_PROFILE`.
4. Push to `main` → GitHub Actions builds, tests, and deploys automatically.

### Post-deployment

```bash
# Run EF migrations against Azure SQL
dotnet ef database update \
  --connection "Server=<sqlServerFqdn>;Database=DlangezwaHS;User ID=sqladmin;Password=<PASS>;Encrypt=True;"
```

The seed data (Admin user, grades, subjects, fee types, email templates) runs automatically on first startup.

---

## Payment Gateway

The application ships with a **mock sandbox** (`MockPaymentGateway`) that simulates the full payment flow without any real money. To swap in a real gateway:

1. Implement `IPaymentGateway` in a new class (e.g., `PayFastGateway`).
2. Register it in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IPaymentGateway, PayFastGateway>();
   ```
3. Add configuration keys to Azure Key Vault or App Settings:
   ```
   Payment__PayFast__MerchantId = ...
   Payment__PayFast__MerchantKey = ...
   ```
4. Never commit API keys to source control.

---

## Running Unit Tests

```bash
dotnet test tests/DlangezwaHS.Tests/DlangezwaHS.Tests.csproj --verbosity normal
```

### Test coverage

| Test class | Scenarios |
|-----------|-----------|
| `ApplicationServiceTests` | Submit, duplicate ID reuse, approve, reject, filter by parent |
| `EnrollmentServiceTests` | Enrol, pending-app guard, get active enrollment |
| `AllocationServiceTests` | Allocate, occupied-bed guard, get available beds, deallocate |
| `PaymentServiceTests` | Manual payment, initiate online, complete online |
| `MockPaymentGatewayTests` | Initiate, verify success, verify failure |
| `DocumentServiceTests` | Null file validation |
| `FullWorkflowIntegrationTests` | Complete apply→approve→enrol→allocate→pay end-to-end |

---

## ERD Diagram

```
ApplicationUser (Identity)
│  Id (PK), FirstName, LastName, Email, Phone
│
├──< Learner
│       Id (PK), FirstName, LastName, DOB, Gender, LearnerIdNumber, ParentId (FK→User)
│       │
│       ├──< LearnerApplication
│       │       Id (PK), LearnerId (FK), ParentId (FK), Status, SubmittedAt
│       │       ReviewedAt, ReviewedByUserId, RejectionReason, [DocPaths×3]
│       │       │
│       │       └──< Enrollment
│       │               Id (PK), LearnerId (FK), ClassId (FK), ApplicationId (FK), EnrolledAt
│       │               │
│       │               └──< EnrollmentSubject (join)
│       │                       EnrollmentId (FK), SubjectId (FK)
│       │
│       ├──< RoomAllocation
│       │       Id (PK), LearnerId (FK), RoomId (FK), BedId (FK), AllocatedAt
│       │
│       └──< Payment
│               Id (PK), LearnerId (FK), ParentId (FK), Amount, Type, Status, Method
│               ProviderRef, BankRef, PaidAt, RecordedByUserId
│
Grade
│  Id (PK), Name, Level
│
└──< Class
        Id (PK), GradeId (FK), Section, Capacity

Subject
│  Id (PK), Name, Code, IsActive
│
└──< SubjectGrade (join)   SubjectId, GradeId

Teacher
│  Id (PK), FirstName, LastName, Email, Phone
│
└──< TeacherSubject (join) TeacherId, SubjectId

Room
│  Id (PK), Name, Capacity, IsActive
│
└──< Bed
        Id (PK), RoomId (FK), BedNumber, Status (Available|Occupied|Maintenance)

RegistrationProof
    Id (PK), EnrollmentId (FK), PaymentId (FK), PdfPath, GeneratedAt, EmailSent

FeeType
    Id (PK), Name, Amount, Description

EmailTemplate
    Id (PK), TemplateKey, Subject, Body, UpdatedAt

AuditLog
    Id (PK), UserId, UserName, Action, EntityType, EntityId, Details, IpAddress, Timestamp
```

---

## Security Notes

- All endpoints are protected with `[Authorize(Roles = "Admin")]` or `[Authorize(Roles = "Parent")]`.
- Anti-forgery tokens on all POST forms.
- Password hashing via ASP.NET Core Identity (PBKDF2).
- Account lockout after 5 failed attempts (15-minute lockout).
- HTTPS enforced; HSTS enabled in production.
- Sensitive config (SMTP password, payment keys) stored in Azure Key Vault, never in source code.
- Uploaded files validated for type (PDF/JPG/PNG) and size (max 5 MB).
- Audit log captures every significant admin action with timestamp and IP.

---

*Built for Dlangezwa High School, KwaZulu-Natal, South Africa.*
