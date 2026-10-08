# Boarding Life Management — Build Instructions for Claude Code

## Project Context
- ASP.NET Core MVC, .NET 8, Entity Framework Core, Azure SQL
- QuestPDF for PDF generation (already installed)
- Bootstrap 5 + Bootstrap Icons (already in layout)
- Existing roles: Admin, Teacher, Parent, Learner
- Existing patterns: follow the same service/controller/view structure as TeacherService, QuestionPaperService
- Brand colors: Navy #0B2A4A | Light Blue #97C8E9 | Grey #6B7280

## New Roles to Add
- **Housemaster** — same registration pattern as Teacher (Admin creates account, system emails credentials)
- **KitchenStaff** — same registration pattern as Teacher (Admin creates account, system emails credentials)
- Add both roles to AspNetRoles via SeedData or migration
- Add Housemaster and KitchenStaff nav menus to _Layout.cshtml following existing role-based nav pattern

---

## UC1 — Student Check-In

### What to build
Record a boarding learner returning to the boarding house via QR code scan.

### Models needed
```
BoardingMovement {
  Id, LearnerId, MovementType (enum: CheckIn/CheckOut),
  QrHash (string 64), ScannedAt, ExpectedReturnTime (nullable),
  ActualReturnTime (nullable), IsLate (bool), LateMinutes (int),
  Status (enum: OnTime/Late/Missing), Notes,
  ApprovedByHousemasterId (nullable), ApprovedByParentUserId (nullable),
  Destination (nullable), Purpose (nullable)
}
```

### Flow
1. Learner presents QR code at scanner
2. System reads hash → looks up learner in BoardingMovements
3. System finds the open CheckOut record for this learner
4. System compares current time to ExpectedReturnTime
5. If late → calculates LateMinutes, sets IsLate = true
6. System records CheckIn, closes the open movement record
7. System notifies Housemaster and Parent if late (email)
8. Housemaster dashboard updates in real time

### Scanner page
- Use html5-qrcode library (already used in EventManagement scanner)
- Route: GET /Boarding/Scanner — Housemaster only
- AJAX endpoint: POST /Boarding/ProcessScan → returns JSON {isValid, learnerName, status, message}
- Show green (on time) or red (late) result card after scan

### Views needed
- `/Boarding/Scanner` — QR scanner for Housemaster (mirrors EventManagement/Scanner.cshtml)
- `/Boarding/MovementLog` — list of today's movements, Housemaster only
- `/Boarding/Dashboard` — Housemaster dashboard showing who is in/out

---

## UC2 — Student Check-Out

### What to build
Record a boarding learner leaving the boarding house. Requires both parent AND housemaster approval before checkout is allowed.

### Additional model fields (same BoardingMovement model)
- LeaveRequestStatus (enum: PendingParent, PendingHousemaster, FullyApproved, Rejected)
- ParentApprovedAt (nullable DateTime)
- HousemasterApprovedAt (nullable DateTime)
- RejectionReason (nullable string)

### Leave Request model
```
LeaveRequest {
  Id, LearnerId, RequestedByParentUserId,
  Destination, Purpose, DepartureDate, ExpectedReturnDate,
  Status (enum: PendingHousemaster/Approved/Rejected),
  HousemasterNotes, CreatedAt, ApprovedAt
}
```

### Flow
1. Parent submits leave request through parent portal (destination, purpose, dates)
2. System notifies Housemaster of pending request
3. Housemaster reviews and approves or rejects
4. System notifies parent of outcome
5. On departure day: learner scans QR at gate
6. System checks LeaveRequest is approved for today
7. System confirms learner is currently checked-in (not already on leave)
8. System records CheckOut, sets ExpectedReturnTime from leave request
9. System notifies Housemaster and Parent that learner has departed

### Views needed
- `/Parent/LeaveRequest/Create` — parent submits leave request (learnerId in query string)
- `/Boarding/LeaveRequests` — Housemaster sees pending requests, can approve/reject
- Add "Request Leave" link in parent LearnerOverview tabs

---

## UC3 — Manage Dietary Preferences & Allergies

### What to build
Learner dietary profiles submitted by parents, reviewed and approved by Kitchen Staff.

### Models needed
```
DietaryProfile {
  Id, LearnerId, SubmittedByParentUserId,
  Status (enum: Pending/Active/Rejected/Superseded),
  SubmittedAt, ApprovedAt, ApprovedByUserId,
  RejectionReason (nullable), Notes
}

DietaryItem {
  Id, DietaryProfileId,
  Category (enum: Allergy/DietaryRequirement/MedicalRestriction/Preference),
  Name (e.g. "Peanuts", "Halal", "Vegetarian"),
  Severity (enum: Mild/Severe/Anaphylactic — null if not an allergy),
  Notes
}
```

### Business rules
- Only one Active profile per learner at a time
- New submission → old profile moves to Superseded when new one is approved
- Severe/Anaphylactic allergies display with red badge everywhere in the meal module
- Kitchen Staff sees active dietary profile on every learner-related meal screen

### Conflict detection (important feature — not CRUD)
- When Kitchen Staff approves a profile, system automatically scans all upcoming MealPlans
- If a meal contains an ingredient matching a Severe/Anaphylactic allergy → flag to Kitchen Staff AND Housemaster
- Show conflict warnings on the meal plan screen

### Views needed
- `/Parent/DietaryProfile` — parent views/submits profile for their learner
- `/KitchenStaff/DietaryProfiles` — list of pending profiles for review
- `/KitchenStaff/DietaryProfile/Review/{id}` — approve or reject with notes

---

## UC4 — Create Weekly Meal Plan

### What to build
Kitchen Staff creates weekly breakfast/lunch/dinner menu. System checks allergy conflicts before publishing.

### Models needed
```
MealPlan {
  Id, WeekStartDate, Status (enum: Draft/Published/Archived),
  TotalBudget (decimal), CreatedByUserId, PublishedAt
}

MealPlanItem {
  Id, MealPlanId, DayOfWeek (1-7), MealType (enum: Breakfast/Lunch/Dinner),
  MenuDescription, Ingredients (string — comma separated),
  ServingTime (string e.g. "07:30"), EstimatedCostPerHead (decimal)
}
```

### Business rules
- Fixed weekly budget configured in Settings (Admin sets it)
- System calculates total estimated cost vs budget and warns if over
- Before publishing: system scans all active learner dietary profiles
- Any item whose Ingredients contain a word matching a Severe/Anaphylactic allergy → show conflict list
- Kitchen Staff must acknowledge conflicts before publishing

### Key feature (not CRUD)
On the publish screen, show:
- Budget tracker: estimated cost vs allocated budget (progress bar)
- Allergy conflict report: list of meals with conflicts and which learners are affected
- Kitchen Staff must tick "I have reviewed all conflicts" before Publish button becomes active

### Views needed
- `/KitchenStaff/MealPlan/Index` — list of weekly meal plans
- `/KitchenStaff/MealPlan/Create` — create new weekly plan (7 days × 3 meals grid)
- `/KitchenStaff/MealPlan/Publish/{id}` — conflict check + budget check before publishing
- `/Parent/MealPlan` — read-only view of current week's menu for parents

---

## UC5 — Student Meal Pre-Ordering

### What to build
Parents pre-order meals for their learner. System enforces 48-hour deadline, allergy checks, and kitchen capacity.

### Models needed
```
MealPreOrder {
  Id, MealPlanItemId, LearnerId, OrderedByParentUserId,
  Status (enum: Pending/Confirmed/Cancelled/WaitListed),
  OrderedAt, PortionSize (enum: Regular/Large/Small),
  BlockedReason (nullable — e.g. "Allergy conflict: Peanuts")
}
```

### Business rules
- Deadline: 48 hours before the meal's ServingTime — system blocks orders after deadline
- If past deadline: show message "Deadline passed. Contact Housemaster to request manual override."
- If allergy conflict: system blocks the selection and shows safe alternatives from the same meal plan
- Kitchen capacity: Admin configures max pre-orders per meal in Settings
- If at capacity: learner is waitlisted, notified if a spot opens

### Key features
- Allergy check happens client-side (instant) AND server-side (on save)
- Blocked items shown in red with reason
- Safe alternatives shown in green

### Views needed
- `/Parent/PreOrder` — parent selects meals for the week for their learner
- `/KitchenStaff/PreOrders` — kitchen sees total portions needed per meal, per day

---

## UC6 — Inventory & Ingredient Management

### What to build
Track food stock. When pre-orders are finalised, system auto-generates a purchase requisition for shortfall ingredients.

### Models needed
```
Ingredient {
  Id, Name, Unit (enum: Kg/Litres/Units/Grams),
  CurrentStock (decimal), MinimumStock (decimal),
  LastUpdatedAt, LastUpdatedByUserId
}

PurchaseRequisition {
  Id, GeneratedAt, GeneratedByUserId,
  Status (enum: Draft/Submitted/Fulfilled),
  Notes
}

PurchaseRequisitionItem {
  Id, PurchaseRequisitionId, IngredientId,
  QuantityRequired, QuantityInStock, QuantityToOrder, UnitCost
}

StockUsageLog {
  Id, IngredientId, QuantityUsed, UsedForMealPlanItemId,
  RecordedAt, RecordedByUserId
}
```

### Key feature (not CRUD — this is the intelligence)
Auto-generate purchase requisition:
1. When Kitchen Staff finalises pre-orders for a week
2. System calculates total ingredients needed (from MealPlanItems × pre-order counts)
3. System compares against CurrentStock for each ingredient
4. Shortfall = needed − in stock
5. System generates PurchaseRequisition with all shortfall items pre-filled
6. Kitchen Staff reviews and submits — they don't calculate anything manually

### Views needed
- `/KitchenStaff/Inventory` — current stock levels with low-stock alerts
- `/KitchenStaff/Inventory/Requisition/{id}` — auto-generated purchase requisition
- Low stock = CurrentStock < MinimumStock → show red badge on ingredient

---

## UC7 — Meal Preparation & Kitchen Schedule

### What to build
System generates kitchen prep schedule from confirmed pre-order totals. Admin assigns tasks to Kitchen Staff.

### Models needed
```
KitchenSchedule {
  Id, MealPlanItemId, ScheduledDate, MealType,
  Status (enum: Scheduled/InProgress/Completed/Delayed),
  PublishedAt, PublishedByUserId
}

KitchenTask {
  Id, KitchenScheduleId, AssignedToUserId,
  TaskDescription, StartTime (string), EndTime (string),
  PortionsRequired (int), Status (enum: Assigned/InProgress/Done),
  CompletedAt (nullable), DelayReason (nullable)
}
```

### Flow
1. System retrieves confirmed pre-order totals for the day
2. Admin reviews and system displays total portions per meal
3. Admin assigns tasks to registered Kitchen Staff
4. System checks for scheduling conflicts (same staff, overlapping times)
5. Admin publishes schedule → Kitchen Staff receive email notification
6. Kitchen Staff update task progress through their portal
7. If a task is marked Delayed → system alerts Admin

### Views needed
- `/Admin/KitchenSchedule` — Admin creates/publishes kitchen schedules
- `/KitchenStaff/MyTasks` — Kitchen Staff sees their assigned tasks for the day, can update progress

---

## UC8 — Meal Attendance Tracking

### What to build
Learners scan QR code at the dining hall entrance. System marks attendance, flags repeated skippers.

### Models needed
```
MealAttendance {
  Id, MealPlanItemId, LearnerId,
  Status (enum: Present/Absent/Late/Unauthorised),
  ScannedAt (nullable), ScannedByUserId (nullable),
  IsManualOverride (bool), OverrideReason (nullable)
}

MealAbsenceAlert {
  Id, LearnerId, ConsecutiveMissedMeals (int),
  AlertSentAt, AlertedHousemasterId, ResolvedAt (nullable)
}
```

### Business rules
- 3 consecutive missed meals → system automatically creates MealAbsenceAlert and notifies Housemaster
- Kitchen Staff can manually mark a learner present if scanner fails
- Duplicate scan (same learner, same meal) → system rejects with message "Already recorded"

### Key feature
Real-time dashboard during meal service showing:
- Total expected: X
- Scanned so far: Y
- Not yet arrived: Z (list with names)
- Flagged (not eligible or duplicate): W

### Scanner
- Same html5-qrcode pattern as EventManagement scanner and Boarding scanner
- Route: GET /KitchenStaff/MealScanner/{mealPlanItemId}
- AJAX: POST /KitchenStaff/RecordMealScan

### Views needed
- `/KitchenStaff/MealScanner/{mealPlanItemId}` — live scanner during meal service
- `/KitchenStaff/MealAttendance` — attendance history and reports
- `/Boarding/Dashboard` — Housemaster sees missed meal alerts

---

## UC9 — Meal Quality & Feedback

### What to build
Anonymous meal ratings after each meal. System detects when average drops below threshold and auto-alerts.

### Models needed
```
MealFeedback {
  Id, MealPlanItemId,
  Rating (int 1-5),
  Comment (nullable string 500),
  SubmittedAt,
  -- NO LearnerId or UserId — fully anonymous
}
```

### Business rules
- Feedback is 100% anonymous — no user ID stored
- Threshold: if average rating < 2.5 across 5+ submissions → auto-alert to Kitchen Staff and Housemaster
- Alert created once per meal — not repeated for every new submission
- Parents can submit feedback through parent portal within 24 hours of the meal's serving time

### Key feature (not CRUD)
Auto-alert on threshold breach:
- System recalculates average after every new submission
- If threshold crossed → MealQualityAlert created in DB
- Kitchen Staff and Housemaster receive email
- Alert visible on both dashboards with the meal name and average score

### Views needed
- `/Parent/MealFeedback/{mealPlanItemId}` — anonymous rating form (1-5 stars + optional comment)
- `/KitchenStaff/FeedbackDashboard` — aggregate scores per meal, alert history
- Show weekly average trend chart using Chart.js

---

## UC10 — Generate Meal Compliance Report

### What to build
On-demand report aggregating all meal module data. Generated as PDF using QuestPDF.

### Report sections
1. **Attendance Summary** — % attendance per meal type per day, trend over selected period
2. **Pre-order vs Actual** — how many pre-ordered vs how many attended (over/under production)
3. **Dietary Compliance** — any meal served that conflicted with an approved allergy profile
4. **Food Waste Estimate** — portions prepared minus portions served
5. **Budget vs Actual** — estimated cost vs recorded actual spend
6. **Satisfaction Scores** — average ratings per meal type, complaint count
7. **Anomaly Flags** — learners with 3+ consecutive missed meals, meals rated below 2.5, budget overruns

### Business rules
- Generated on demand by Housemaster or Admin
- Date range selector: today / this week / this month / custom
- Export as PDF (QuestPDF) and optionally Excel
- Archived — every generated report saved with timestamp and who generated it

### Views needed
- `/Boarding/MealComplianceReport` — Housemaster generates and views report
- PDF download via `/Boarding/MealComplianceReport/Download?from=X&to=Y`

---

## SQL Migration
Create a single EF migration called `Increment3_BoardingMeals` covering all new tables above.
Run `dotnet ef migrations add Increment3_BoardingMeals` then `dotnet ef database update`.

## Registration for New Roles
- Add `HousemasterRegistration` and `KitchenStaffRegistration` actions to AdminController
- Follow the exact same pattern as Teacher registration (AdminController.RegisterTeacher)
- After creating the account: send a welcome email with credentials using IEmailService

## Navigation
- Housemaster nav: Dashboard | Check-In/Out Scanner | Leave Requests | Meal Absence Alerts
- Kitchen Staff nav: Meal Plans | Pre-Orders | Inventory | Kitchen Schedule | Meal Scanner | Feedback
- Parent nav: add Leave Request and Meal Pre-Order and Meal Feedback links under My Learners dropdown
- Admin nav: add Kitchen Schedule and Boarding Settings under existing dropdowns
