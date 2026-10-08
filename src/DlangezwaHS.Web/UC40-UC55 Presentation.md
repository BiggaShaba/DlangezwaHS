# Dlangezwa High School System — Use Cases UC40 to UC55

**Modules presented:** H. Boarding Life (UC40–UC43) · I. Meals & Kitchen (UC44–UC55)
**Date:** 7 October 2026
**Status:** Describes what is built in the system today.

---

## 1. Overview

| UC | Use Case | Module | Primary Actor(s) |
|---|---|---|---|
| UC40 | Issue Boarding QR Badge | Boarding | Housemaster, Parent |
| UC41 | Request & Approve Leave | Boarding | Parent, Housemaster |
| UC42 | Check-Out & Check-In (QR Scan) | Boarding | Housemaster |
| UC43 | Housemaster Dashboard & Movement Log | Boarding | Housemaster |
| UC44 | Manage Dietary Profile & Allergies | Meals | Parent / Learner |
| UC45 | Manage Ingredients & Meal Library | Meals | Kitchen Staff |
| UC46 | Create & Publish the Weekly Meal Plan | Meals | Kitchen Staff |
| UC47 | Pre-Order Meals | Meals | Parent / Learner |
| UC48 | Finalise Pre-Orders & Generate Purchase Requisition | Meals | Kitchen Staff |
| UC49 | Receive Stock | Meals | Kitchen Staff |
| UC50 | Record Ingredient Usage & Leftovers | Meals | Kitchen Staff |
| UC51 | Kitchen Teams & Schedule | Meals | Admin |
| UC52 | Update Kitchen Tasks | Meals | Kitchen Staff |
| UC53 | Meal Attendance (Dining Hall Scanning) | Meals | Kitchen Staff |
| UC54 | Meal Feedback & Quality Alerts | Meals | Parent / Learner, Kitchen Staff |
| UC55 | Meal Compliance Report | Meals | Housemaster |

### Actors involved

| Actor | Role in these use cases |
|---|---|
| **Parent** | Views the QR badge, requests leave, manages dietary profile, pre-orders meals, rates meals |
| **Learner** | Manages dietary profile, pre-orders meals, rates meals, is scanned at the gate and dining hall |
| **Housemaster** | Issues badges, approves leave, scans check-in/out, supervises boarders, generates the compliance report |
| **Kitchen Staff** | Manages ingredients, meals, plans, stock, usage, tasks, meal scanning and feedback |
| **Admin** | Creates kitchen teams and publishes the kitchen schedule |
| **System** | Generates codes, sends emails, calculates costs/stock/staffing, raises alerts |

### How the modules connect

```
                ┌──────────────── QR Badge (UC40) ────────────────┐
                ▼                                                 ▼
  Leave (UC41) → Gate Check-Out/In (UC42) → Dashboard (UC43)   Dining Hall Scan (UC53)
                                                ▲                 │
                                                │ missed-meal     │
                                                └── alerts ───────┘

  Dietary Profile (UC44) ──allergy checks──┐
  Ingredients & Meals (UC45) ──────────────┼─→ Weekly Meal Plan (UC46) → Pre-Orders (UC47)
                                           │                                   │
                                           │       Finalise + Requisition (UC48)
                                           │                                   │
                       Receive Stock (UC49) ← purchase ─────────────────────────┘
                                           │
         Kitchen Schedule (UC51) → Tasks (UC52) → Usage & Leftovers (UC50)
                                           │
                     Serve → Scan (UC53) → Feedback (UC54) → Compliance Report (UC55)
```

---

# H. Boarding Life

## UC40. Issue Boarding QR Badge

| | |
|---|---|
| **Brief Description** | Each boarding learner gets a unique QR badge used for check-in/out and meals. |
| **Actors** | Housemaster, Parent |
| **Triggering Event** | Learner is allocated a bed. |
| **Preconditions** | Learner has an active bed allocation. |

**Flow of Activities**
1. Housemaster opens **Boarders & QR Badges**, or Parent opens **QR Badge** from the learner overview.
2. System generates a secure random code the first time and shows the QR badge (printable).
3. The same badge is used at the boarding gate (UC42) and in the dining hall (UC53).

**Alternative Flows**
- Parent of a non-boarding learner → "QR badges are only available for boarding learners."

**Key Features**
- Secure, randomly generated code (created once, reused afterwards)
- Printable badge
- One badge for both gate access and dining hall

---

## UC41. Request & Approve Leave

| | |
|---|---|
| **Brief Description** | A parent requests leave for a boarder; the housemaster approves or rejects it. |
| **Actors** | Parent (requests), Housemaster (approves), System (emails) |
| **Triggering Event** | Learner needs to leave the boarding house (weekend, appointment, etc.). |
| **Preconditions** | Learner is a boarder. |

**Flow of Activities**
1. Parent opens the learner overview and clicks **Request Leave**.
2. Parent enters destination, purpose, departure date and expected return date, then submits.
3. System saves the request as **Pending** and emails all housemasters.
4. Housemaster opens **Leave Requests** (pending, or show all).
5. Housemaster clicks **Approve** (optional notes) or **Reject** (reason).
6. System records the decision and emails the parent the outcome.
7. Parent sees the status and notes on the learner overview.

**Alternative Flows**
- 1a. Learner is not a boarder → "Leave requests are only available for boarding learners."
- 2a. Return date before departure date → error shown.

**Key Features**
- Leave statuses: **Pending → Approved / Rejected**
- Email to housemasters on submission, email to parent on decision
- Housemaster notes / rejection reason visible to the parent

---

## UC42. Check-Out & Check-In (QR Scan)

| | |
|---|---|
| **Brief Description** | The housemaster scans a learner's badge when they leave and return; late returns are flagged. |
| **Actors** | Housemaster (primary), Parent (notified) |
| **Triggering Event** | Learner leaves on approved leave, or returns. |
| **Preconditions** | Learner has a QR badge. |

**Flow of Activities**
1. Housemaster opens **Check-In/Out Scanner** and scans the learner's badge.
2. System looks at the learner's last movement:
   - **Currently in → Check-out:** system looks for an **approved** leave request covering today that hasn't been used yet. If found, it records the check-out with the expected return date and emails the housemasters and parent that the learner has departed.
   - **Currently out → Check-in:** system records the return and compares it with the expected return time. If late, it records the minutes late and emails the housemasters and parent.
3. Screen shows a green (on time) or red (late / refused) result.

**Alternative Flows**
- No approved leave for today → "No approved leave request for today. Checkout denied."
- Leave already used → "This leave request has already been used to check out."
- Unknown QR → "QR code not recognised."

**Key Features**
- Single scanner that automatically decides check-out vs check-in
- A learner can only leave on an approved, unused leave request
- Late returns recorded in minutes, with email alerts
- Colour-coded result (green / red)

---

## UC43. Housemaster Dashboard & Movement Log

| | |
|---|---|
| **Brief Description** | The housemaster monitors who is in or out and the day's alerts. |
| **Actors** | Housemaster |
| **Triggering Event** | Daily supervision. |
| **Preconditions** | None. |

**Flow of Activities**
1. Housemaster opens **Dashboard**: total boarders, pending leave requests, late returns today, learners currently out (and whether overdue), and missed-meal alerts.
2. Housemaster opens **Movement Log** and picks a date to see every check-in/out with status, destination and purpose.
3. Housemaster can also view active **Dietary Profiles** and their documents (read-only), missing-learner bus alerts (UC31) and the meal compliance report (UC55).

**Key Features**
- Dashboard counters: total boarders, pending leave, late returns, learners out / overdue, missed-meal alerts
- Date-filtered movement history
- Read-only access to dietary profiles and medical documents
- Links to bus alerts and the meal compliance report

---

# I. Meals & Kitchen

## UC44. Manage Dietary Profile & Allergies

| | |
|---|---|
| **Brief Description** | Parent or learner records allergies and dietary needs; the profile is active immediately and checked against meals. |
| **Actors** | Parent or Learner (primary), Kitchen Staff and Housemaster (notified, view) |
| **Triggering Event** | Learner has an allergy, medical restriction or dietary requirement. |
| **Preconditions** | Learner exists. |

**Flow of Activities**
1. Parent (or learner) opens **Dietary Profile → Add / Update**.
2. Adds one or more items: category (**Allergy, Dietary Requirement, Medical Restriction, Preference**), name (e.g. Peanuts, Halal), severity for allergies (**Mild, Severe, Anaphylactic**) and notes.
3. Optionally uploads supporting documents (PDF or image).
4. Clicks **Save**.
5. System makes the new profile **Active** and marks the previous one **Superseded**.
6. System emails kitchen staff and housemasters. If the learner added it, the parent is also emailed.
7. System checks upcoming **published** meal plans; if a meal contains an ingredient matching a **Severe/Anaphylactic** allergy, it emails an **allergy conflict** alert to kitchen staff and housemasters.

**Business Rules**
- Only one Active profile per learner.
- Medical documents are stored outside the public web folder and only served to the parent, the learner, kitchen staff and housemasters.

**Key Features**
- 4 categories and 3 allergy severity levels
- Profile versioning (Active / Superseded)
- Secure medical document storage
- Automatic allergy-conflict check against published menus

---

## UC45. Manage Ingredients & Meal Library

| | |
|---|---|
| **Brief Description** | Kitchen staff maintain ingredients and recipes, which drive cost, allergy checks and stock. |
| **Actors** | Kitchen Staff |
| **Triggering Event** | New dish or ingredient. |
| **Preconditions** | None. |

**Flow of Activities**
1. **Ingredients** (Inventory page): add/edit name, unit (Kg, Litres, Units, Grams), unit cost and minimum stock. New ingredients start at **zero** stock.
2. **Meal library:** open **Meals → Add/Edit**, enter the meal name, prep minutes per serving (used for kitchen staffing), optional image, and the ingredients with **quantity per serving**.
3. System calculates the **cost per serving** from ingredient costs.
4. Meals can be deactivated/reactivated.
5. Editing a recipe updates plan items that use it, so menus, costs and allergy checks stay correct.

**Business Rules**
- At least one ingredient per meal.
- Each ingredient listed once per meal.
- Ingredient names must be unique.
- Meal image: JPG/PNG/WEBP/GIF under 5 MB.

**Key Features**
- Automatic cost-per-serving calculation
- Recipes drive stock deduction (UC50), requisitions (UC48), staffing (UC51) and allergy checks (UC44/46/47)

---

## UC46. Create & Publish the Weekly Meal Plan

| | |
|---|---|
| **Brief Description** | Kitchen staff plan a week of meals, check the budget and allergies, and publish the menu. |
| **Actors** | Kitchen Staff (primary), Parent and Learner (view menu) |
| **Triggering Event** | Planning the coming week(s). |
| **Preconditions** | Meal library and weekly budget (Settings) exist. |

**Flow of Activities**
1. Kitchen staff open **Meal Plans → Create** and choose a week (Monday–Sunday).
2. For each day, they choose breakfast, lunch and dinner from the meal library (several options per meal are allowed).
3. Clicks **Save** → plan is saved as **Draft**.
4. Clicks **Publish**; system shows:
   - **Budget tracker:** estimated cost vs weekly budget
   - **Allergy conflict report:** meals containing a Severe/Anaphylactic allergen and the affected learners
5. Kitchen staff tick **"I have reviewed all conflicts"** and click **Publish**.
6. Parents and learners can view **this week's and next week's** menu.

**Business Rules**
- Plans can be made for the current week and up to **8 weeks ahead**; one plan per week; past weeks cannot be created or changed.
- Days that have already passed are locked.
- Publishing is blocked until conflicts are acknowledged.

**Key Features**
- Draft → Published workflow
- Multiple options per meal service
- Budget tracker and allergy conflict report before publishing

---

## UC47. Pre-Order Meals

| | |
|---|---|
| **Brief Description** | Parents or learners choose which meal option the learner will eat. |
| **Actors** | Parent or Learner |
| **Triggering Event** | A menu is published. |
| **Preconditions** | Published meal plan with upcoming meals. |

**Flow of Activities**
1. User opens **Pre-Order Meals**; system lists the upcoming meals that are still open.
2. User picks an option and portion size (Small, Regular, Large) and clicks **Order**.
3. System checks the deadline, the learner's allergies and the kitchen's capacity.
4. Order is saved as **Confirmed**, or **Wait-listed** if the meal is at capacity.
5. Choosing a different option for the same meal service replaces the earlier choice.
6. User can **cancel** an order.

**Business Rules**
- Orders close **48 hours before** serving time: "Ordering for this meal has closed. Contact the Housemaster to request a manual override."
- Allergy conflict blocks the order: "Allergy conflict: {allergen}. Please choose another option."
- Capacity = **maximum pre-orders per meal** in Settings.

**Key Features**
- Portion sizes (Small / Regular / Large)
- 48-hour cut-off, allergy blocking, capacity limit with wait-list
- One order per learner per meal service (re-ordering replaces)

---

## UC48. Finalise Pre-Orders & Generate Purchase Requisition

| | |
|---|---|
| **Brief Description** | Kitchen staff close the week's orders and the system works out what must be bought. |
| **Actors** | Kitchen Staff |
| **Triggering Event** | Ordering period ends. |
| **Preconditions** | Published plan with pre-orders. |

**Flow of Activities**
1. Kitchen staff open **Pre-Orders** for a plan and see confirmed and wait-listed counts per meal.
2. Click **Finalise**.
3. System confirms outstanding orders, then calculates ingredients needed = **recipe quantity per serving × confirmed orders**.
4. System compares that with current stock and creates a **Draft purchase requisition** for the shortfall only.
5. Kitchen staff review the requisition and click **Submit**.

**Key Features**
- Automatic ingredient demand calculation from recipes
- Requisition only for the **shortfall** (needed − in stock)
- Draft → Submitted requisition

---

## UC49. Receive Stock

| | |
|---|---|
| **Brief Description** | Stock only increases when a delivery is captured with its receipt or invoice. |
| **Actors** | Kitchen Staff |
| **Triggering Event** | A delivery arrives. |
| **Preconditions** | Ingredients exist. |

**Flow of Activities**
1. Kitchen staff open **Receive Stock**.
2. Upload a photo or PDF of the receipt/invoice and add each ingredient and quantity received.
3. Click **Save**; system adds the quantities to stock and keeps the receipt for viewing later.
4. Inventory shows each ingredient with a **low stock** flag when below its minimum.

**Business Rules**
- Receipt file is required (JPG, PNG, WEBP or PDF, under 10 MB) with at least one ingredient line.

**Key Features**
- Proof-of-delivery required for every stock increase (audit trail)
- Low-stock flags against minimum stock levels

---

## UC50. Record Ingredient Usage & Leftovers

| | |
|---|---|
| **Brief Description** | After cooking, the chef records servings prepared; the system deducts ingredients using the recipe. |
| **Actors** | Kitchen Staff |
| **Triggering Event** | A meal has been prepared. |
| **Preconditions** | The meal has a recipe in the library. |

**Flow of Activities**
1. Kitchen staff open **Record Usage** and choose a meal (from a week ago up to tomorrow).
2. System suggests the number of servings ordered; staff enter the actual servings prepared and notes.
3. System deducts **recipe quantity × servings** from each ingredient (stock never goes below zero).
4. After service, staff record **leftover servings** (between 0 and servings prepared) for waste tracking.

**Key Features**
- Automatic recipe-based stock deduction
- Suggested servings from pre-orders
- Leftover tracking feeds the food-waste estimate in UC55

---

## UC51. Kitchen Teams & Schedule

| | |
|---|---|
| **Brief Description** | Admin assigns a kitchen team to prepare each meal and publishes the schedule. |
| **Actors** | Admin (primary), Kitchen Staff (notified) |
| **Triggering Event** | Meals need to be staffed. |
| **Preconditions** | Kitchen staff exist; a published meal plan with pre-orders exists; shift times are set in Settings. |

**Flow of Activities**
1. Admin creates **Kitchen Teams** (unique name, at least one member) and can activate/deactivate them.
2. Admin opens **Kitchen Schedule → Create** and chooses a meal and a team.
3. System fills in the **portions from confirmed pre-orders** and the **shift times** for that meal type.
4. System recommends staff numbers: **portions × prep minutes per serving ÷ shift length** (minimum 1).
5. Admin keeps or removes team members and clicks **Save**.
6. System checks no staff member is on an overlapping shift that day, then saves the schedule.
7. Admin clicks **Publish**; system emails each assigned staff member.

**Alternative Flows**
- 6a. Fewer staff than recommended → warning; Admin must add staff or confirm the shortfall.
- 6b. Overlapping shift → "{Name} is already on an overlapping kitchen shift on {date}."

**Key Features**
- Staffing recommendation formula based on real demand
- Overlapping-shift prevention
- Email notification on publish

---

## UC52. Update Kitchen Tasks

| | |
|---|---|
| **Brief Description** | Kitchen staff update the progress of their assigned tasks. |
| **Actors** | Kitchen Staff (primary), Admin (notified on delay) |
| **Triggering Event** | Work on a scheduled meal starts or finishes. |
| **Preconditions** | Schedule has been published. |

**Flow of Activities**
1. Kitchen staff open **Kitchen Schedule (My Tasks)**.
2. Update each task to **Assigned → In Progress → Done**.
3. If running late, they enter a **delay reason**; system marks the schedule **Delayed** and emails all admins.

**Key Features**
- Task status tracking (Assigned / In Progress / Done / Delayed)
- Delay reason with automatic admin email

---

## UC53. Meal Attendance (Dining Hall Scanning)

| | |
|---|---|
| **Brief Description** | Learners' badges are scanned at the dining hall; no-shows are tracked and repeat absences alerted. |
| **Actors** | Kitchen Staff (primary), Housemaster (alerted) |
| **Triggering Event** | A meal service starts. |
| **Preconditions** | Today's meals are in a published plan; learners have QR badges. |

**Flow of Activities**
1. Kitchen staff open **Meal Scanner** and choose today's meal service.
2. Scan each learner's badge:
   - Learner pre-ordered → recorded **Present** against the option they ordered.
   - No pre-order → recorded **Unauthorised**.
   - Already scanned → "Already served for this meal."
3. The live dashboard shows expected, scanned, not yet arrived (names) and flagged learners.
4. After service, staff click **Finalise**; learners who ordered but didn't come are marked **Absent**.
5. If a learner has missed **3 consecutive meals**, system creates a missed-meal alert and emails the housemasters (shown on the housemaster dashboard).
6. **Meal Attendance** shows the history by date.

**Key Features**
- Attendance statuses: Present / Unauthorised / Absent
- Duplicate-scan prevention
- Live service dashboard
- Welfare alert after 3 consecutive missed meals

---

## UC54. Meal Feedback & Quality Alerts

| | |
|---|---|
| **Brief Description** | Parents and learners rate meals anonymously; poor ratings automatically alert staff. |
| **Actors** | Parent or Learner (rate), Kitchen Staff & Housemaster (alerted), Kitchen Staff (resolve) |
| **Triggering Event** | A meal has been served. |
| **Preconditions** | Within 24 hours after serving time. |

**Flow of Activities**
1. User opens the menu and clicks **Rate** on a served meal.
2. Gives 1–5 stars, an optional comment and portion feedback (Too little / Just right / Too much).
3. System saves it **anonymously** (no user stored).
4. System recalculates the meal's average. If there are **5 or more** ratings and the average is **below 2.5**, it creates one quality alert and emails kitchen staff and housemasters.
5. Kitchen staff open **Feedback** to see averages, portion feedback, comments and alerts per meal.
6. Kitchen staff **resolve** an alert by describing what was done.

**Business Rules**
- Outside the 24-hour window → "Feedback can only be submitted within 24 hours of the meal's serving time."
- Only one open alert per meal at a time.

**Key Features**
- Anonymous 1–5 star ratings with portion feedback
- Automatic quality alert (≥ 5 ratings and average < 2.5)
- Alert resolution with a recorded action

---

## UC55. Meal Compliance Report

| | |
|---|---|
| **Brief Description** | On-demand report pulling together all meal data, exported to PDF and archived. |
| **Actors** | Housemaster |
| **Triggering Event** | Weekly/monthly review or an inspection. |
| **Preconditions** | Meal data exists for the period. |

**Flow of Activities**
1. Housemaster opens **Meal Compliance Report** and selects a date range (default last 7 days).
2. System shows:
   1. Attendance summary — % attendance per meal type per day
   2. Pre-ordered vs actually attended
   3. Dietary compliance — meals served containing a learner's severe allergen
   4. Food waste estimate — portions prepared vs served
   5. Budget vs actual
   6. Satisfaction — average rating and complaints per meal type
   7. Anomaly flags — 3+ consecutive missed meals, meals rated below 2.5, budget overruns
3. Housemaster clicks **Download PDF**.
4. System saves an archived copy with the date range, time and who generated it.

**Key Features**
- Seven report sections combining data from UC44–UC54
- PDF export with an archived copy (date range, time, generated by)

---

# Appendix

## A. Automatic Email Notifications (UC40–UC55)

| Event | Use Case | Who is emailed |
|---|---|---|
| Leave request submitted | UC41 | Housemasters |
| Leave approved / rejected | UC41 | Parent |
| Learner checked out / checked in late | UC42 | Housemasters + parent |
| Dietary profile saved | UC44 | Kitchen staff + housemasters (+ parent if the learner added it) |
| Allergy conflict with published meals | UC44 | Kitchen staff + housemasters |
| Kitchen schedule published | UC51 | Assigned kitchen staff |
| Kitchen task delayed | UC52 | Admins |
| 3 consecutive missed meals | UC53 | Housemasters |
| Meal average below 2.5 | UC54 | Kitchen staff + housemasters |

## B. Key Calculations

| Calculation | Formula | Use Case |
|---|---|---|
| Cost per serving | Σ (ingredient quantity per serving × unit cost) | UC45 |
| Ingredients needed | Recipe quantity per serving × confirmed orders | UC48 |
| Purchase requisition | Ingredients needed − current stock (shortfall only) | UC48 |
| Stock deduction | Recipe quantity × servings prepared (never below zero) | UC50 |
| Recommended kitchen staff | Portions × prep minutes per serving ÷ shift length (min. 1) | UC51 |
| Quality alert | ≥ 5 ratings **and** average < 2.5 | UC54 |
| Missed-meal alert | 3 consecutive missed meals | UC53 |

## C. Differences from the Original Specification

| # | Original description | What the system does now |
|---|---|---|
| 1 | Leave needs **parent and housemaster** approval (UC41) | Parent submitting the request counts as the parent's approval; only the housemaster approves |
| 2 | Dietary profiles are **reviewed and approved** by kitchen staff (UC44) | Profiles are **active immediately**; kitchen staff and housemasters are notified and can view them |
| 3 | Wait-listed pre-orders are notified when a spot opens (UC47) | Cancelling an order does **not** promote or notify wait-listed learners |
| 4 | Kitchen staff can **manually mark a learner present** if the scanner fails (UC53) | The manual override exists in the code but has **no screen** yet |
| 5 | Meal compliance report generated by **Housemaster or Admin** (UC55) | Admin's menu links to it, but the page is restricted to **Housemasters**, so Admin gets "Access Denied" |
