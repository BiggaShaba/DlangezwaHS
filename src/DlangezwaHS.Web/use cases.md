# Dlangezwa High School System — Use Case Descriptions

_Last updated: 7 October 2026. Reflects what is built in the system today._

---

## Actors

| Actor | Who they are | How they get an account |
|---|---|---|
| **Visitor** | Anyone browsing the public website | No account needed |
| **Parent** | Parent / guardian of one or more learners | Signs up on the website |
| **Learner** | An enrolled learner | Parent creates the login (signs in with Learner ID) |
| **Admin** | School administration | Seeded on first start-up |
| **Teacher** | Teaching staff (may also be a **Coach** for activities) | Admin employs them; credentials emailed |
| **Housemaster** | Boarding house staff | Admin employs them; credentials emailed |
| **Kitchen Staff** | Kitchen / catering staff | Admin employs them; credentials emailed |
| **Event Coordinator** | A Teacher or Learner given a role on a school event | Admin assigns them per event |
| **System** | Automatic actions (emails, alerts, calculations, scheduled jobs) | — |

---

## Contents

**A. Public website & accounts** — UC1–UC4
**B. Admissions, enrolment & fees** — UC5–UC12
**C. Staff & school settings** — UC13–UC15
**D. Academics** — UC16–UC23
**E. Calendar & school events** — UC24–UC28
**F. Transport** — UC29–UC33
**G. Extracurricular activities** — UC34–UC39
**H. Boarding life** — UC40–UC43
**I. Meals & kitchen** — UC44–UC55
**Appendix** — Email notifications · Differences from the original specification

---

# A. Public Website & Accounts

### UC1. View School Information & Contact the School

| | |
|---|---|
| **Brief Description** | A visitor learns about the school on the landing page and sends an enquiry through the contact form. |
| **Actors** | Visitor (primary), Admin office (receives email) |
| **Triggering Event** | Someone opens the school website. |
| **Preconditions** | School contact details are set in `appsettings.json` → `School`. |

**Flow of Activities**
1. Visitor opens the home page.
2. System shows: about the school, mission and values, academics (Grades 8–9 and 10–12), boarding and student life, the six admission steps, and **Sign Up** / **Sign In** buttons.
3. Visitor clicks **Contact**.
4. System shows the contact form, address, phone, email, office hours and a map.
5. Visitor enters name, email, optional phone, topic (General, Admissions, Boarding, Fees & payments, Transport, Portal/login help) and a message.
6. Visitor clicks **Send message**.
7. System validates the form and emails the enquiry to the school office address.
8. System shows "Thank you — your message has been sent."

**Alternative Flows**
- 7a. Required fields missing or message under 10 characters → errors are shown next to the fields.
- 7b. Email cannot be sent → "We couldn't send your message right now" is shown and the form keeps the input.
- Signed-in users who open the home page are sent straight to their own dashboard.

---

### UC2. Parent Sign Up

| | |
|---|---|
| **Brief Description** | A parent creates an account so they can apply for and manage their children. |
| **Actors** | Parent |
| **Triggering Event** | Parent clicks **Sign Up** on the website. |
| **Preconditions** | None. |

**Flow of Activities**
1. Parent clicks **Sign Up**.
2. Parent enters first name, last name, email, phone, password (minimum 8 characters) and confirms the password.
3. Parent clicks **Register**.
4. System creates the account with the **Parent** role and signs the parent in.
5. System opens the Parent dashboard.

**Alternative Flows**
- 4a. Email already used or password too weak → system shows the reason and the parent corrects it.

---

### UC3. Sign In / Sign Out

| | |
|---|---|
| **Brief Description** | Any user signs in and is taken to the dashboard for their role. |
| **Actors** | Parent, Learner, Admin, Teacher, Housemaster, Kitchen Staff |
| **Triggering Event** | User clicks **Sign In**. |
| **Preconditions** | User has an account. |

**Flow of Activities**
1. User enters **email** (staff and parents) or **Learner ID** (learners) and password, optionally ticking "Remember me".
2. System checks the credentials.
3. System redirects by role: Admin → Admin Dashboard, Teacher → Teacher Dashboard, Learner → Learner Dashboard, Parent → Parent Dashboard (or back to the page they originally requested).
4. To leave, the user opens their name menu and clicks **Sign Out**.

**Business Rules / Alternative Flows**
- Repeated wrong passwords lock the account for 15 minutes.
- A learner whose login was disabled by their parent sees "This account has been disabled. Please speak to your parent."
- Users who open a page their role cannot use see the **Access Denied** page.

---

### UC4. Manage Learner Login (Parent)

| | |
|---|---|
| **Brief Description** | A parent creates, resets, enables or disables their child's own login. |
| **Actors** | Parent (primary), Learner |
| **Triggering Event** | Parent wants their child to use the learner portal, or needs to reset/disable it. |
| **Preconditions** | The learner is **enrolled** (active enrolment). |

**Flow of Activities**
1. Parent opens the dashboard and clicks **Learner Login** on the learner's card.
2. Parent enters a password (and optionally the learner's own email).
3. System creates the login with the learner's **Learner ID** as the username and the **Learner** role.
4. System confirms: "{Name} can now sign in with Learner ID {number}."
5. Later, the parent can **reset the password**, or **disable / enable** access from the dashboard.

**Alternative Flows**
- 1a. Learner not yet enrolled → "A learner login can only be created once the learner is enrolled."
- Disabling locks the account permanently until the parent enables it again.

**What the learner can do once signed in:** view dashboard, class and subjects, timetable, academic report, attendance, question papers, activities and achievements, weekly menu, pre-order meals, give meal feedback and manage their dietary profile (the parent is emailed when the learner adds one).

---

# B. Admissions, Enrolment & Fees

### UC5. Submit Learner Application

| | |
|---|---|
| **Brief Description** | A parent applies for admission for a learner and uploads supporting documents. |
| **Actors** | Parent (primary), Admin (notified via dashboard) |
| **Triggering Event** | Parent wants to enrol a child at the school. |
| **Preconditions** | Parent is signed in. |

**Flow of Activities**
1. Parent clicks **Apply**.
2. Parent enters the learner's first name, last name, date of birth, gender and ID number.
3. Parent uploads three documents: **Learner ID**, **Previous school report**, **Guardian ID**.
4. Parent clicks **Submit**.
5. System validates each document (PDF, JPG or PNG; under 5 MB).
6. System creates the learner (or reuses the existing learner with the same ID number) and saves the application as **Pending**.
7. System emails the parent an "application received" message and writes an audit log entry.
8. System shows "Application submitted successfully. Reference: #{id}".

**Alternative Flows**
- 5a. A document is missing, too large or the wrong type → the error is shown and nothing is saved.

---

### UC6. Review Application (Approve / Reject)

| | |
|---|---|
| **Brief Description** | Admin reviews an application and its documents, then approves or rejects it. |
| **Actors** | Admin (primary), Parent (notified) |
| **Triggering Event** | A new application appears as Pending. |
| **Preconditions** | Application exists with status Pending. |

**Flow of Activities**
1. Admin opens **Admissions → Applications** (filter by Pending / Approved / Rejected / Enrolled, or search by learner name or parent email).
2. Admin opens an application and views the learner details, uploaded documents and its audit history.
3. Admin clicks **Approve**, **or** clicks **Reject** and enters a reason.
4. System updates the status, emails the parent the outcome (with the reason if rejected) and records the action in the audit log with the admin's name and IP address.

---

### UC7. Enrol Learner & Assign Subjects

| | |
|---|---|
| **Brief Description** | Admin places an approved learner into a class and assigns subjects. |
| **Actors** | Admin |
| **Triggering Event** | Application has been approved. |
| **Preconditions** | Application status is **Approved**; grades, classes and subjects exist in Settings. |

**Flow of Activities**
1. From the approved application, Admin clicks **Enrol**.
2. Admin selects a class (e.g. Grade 10 A) and ticks the subjects.
3. Admin clicks **Enrol**.
4. System deactivates any previous enrolment for the learner, creates the new active enrolment and links the subjects.
5. System sets the application to **Enrolled**, writes an audit log entry and opens the Enrolment Detail page.

**Alternative Flows**
- 4a. Application is not Approved → "Application must be Approved before enrolment."

**Related:** Admin can list all active enrolments (search by name) and **Export enrolments to CSV** (name, learner ID, class, subjects, date).

---

### UC8. Manage Rooms & Allocate a Bed

| | |
|---|---|
| **Brief Description** | Admin sets up boarding rooms and beds and allocates a bed to a boarding learner. |
| **Actors** | Admin |
| **Triggering Event** | A learner requires boarding accommodation. |
| **Preconditions** | Rooms with available beds exist. |

**Flow of Activities**
1. **Set up rooms:** Admin opens **Boarding & Rooms → Add Room**, enters name, capacity, notes and number of beds. System creates the room and beds B1, B2, … as Available. Extra beds can be added later; a room can be removed (deactivated).
2. **Allocate:** From the Enrolment Detail page, Admin clicks **Allocate Bed**.
3. Admin selects a room; system loads only the **available** beds in that room.
4. Admin selects a bed and clicks **Allocate**.
5. System releases the learner's previous bed (if any, marking it Available), creates the new allocation and marks the bed **Occupied**.
6. System writes an audit log entry.

**Business Rules**
- A learner with an active bed allocation is treated as a **boarding learner** everywhere else in the system (transport rosters, leave requests, QR badges, accommodation fee).

**Alternative Flows**
- 5a. Bed was taken in the meantime → "Bed {number} is not available."

---

### UC9. Pay Fees Online

| | |
|---|---|
| **Brief Description** | Parent pays the registration or accommodation fee online. |
| **Actors** | Parent (primary), Payment gateway (currently a sandbox/mock gateway) |
| **Triggering Event** | Parent sees an outstanding balance. |
| **Preconditions** | Learner is enrolled; fee types exist in Settings. |

**Flow of Activities**
1. Parent opens the dashboard; system shows the balance per enrolled learner (Registration fee, plus Accommodation fee if a bed is allocated, minus completed payments).
2. Parent clicks **Pay** and chooses the fee type; system fills in the amount from Settings.
3. Parent confirms; system creates a **Pending** online payment and redirects to the gateway.
4. Gateway returns to the school site; system verifies the payment.
5. System marks the payment **Completed** (or **Failed**), records the reference and date, and emails the parent a payment confirmation.
6. Parent sees "Payment successful! Your receipt has been emailed."

---

### UC10. Record Manual (EFT) Payment

| | |
|---|---|
| **Brief Description** | Admin captures a payment made by EFT or at the office. |
| **Actors** | Admin (primary), Parent (notified) |
| **Triggering Event** | Proof of an offline payment is received. |
| **Preconditions** | Learner exists. |

**Flow of Activities**
1. From the Enrolment Detail page, Admin clicks **Record Payment**.
2. Admin enters amount, fee type, bank reference and notes.
3. System saves a completed manual payment, writes an audit entry and emails the parent a confirmation.

**Related:** **Finance → Payments** lists all payments, filterable by Completed / Pending.

---

### UC11. Generate Proof of Registration

| | |
|---|---|
| **Brief Description** | Admin generates the official PDF proof of registration, which is stored and emailed to the parent. |
| **Actors** | Admin (primary), Parent |
| **Triggering Event** | Learner is enrolled (and fees are paid). |
| **Preconditions** | Active enrolment exists. |

**Flow of Activities**
1. On the Enrolment Detail page, Admin clicks **Generate & Email**.
2. System builds a PDF with the learner, class, subjects, room/bed (if boarding) and payment details.
3. System saves the PDF, records it against the enrolment, emails it to the parent, and writes an audit entry.
4. The PDF downloads for the admin.
5. Parent can later download it from the learner's overview page.

---

### UC12. Parent Dashboard & Learner Overview

| | |
|---|---|
| **Brief Description** | Parent sees a summary of each child and drills into one learner's full record. |
| **Actors** | Parent |
| **Triggering Event** | Parent signs in. |
| **Preconditions** | Parent has at least one learner (via an application). |

**Flow of Activities**
1. System shows one card per learner: application status, class, balance due, attendance % (last 30 days), proof available, learner login status.
2. System shows notifications: outstanding balances, attendance **at risk** (60–79%) and **critically low** (below 60%).
3. Parent opens a learner's **Overview**, which shows: personal details, class and subjects, room and bed, leave requests, fees and payment history, proofs of registration, attendance summary, and links to the academic report, attendance history, timetable, QR badge, dietary profile, meal pre-orders and leave request.

---

# C. Staff & School Settings

### UC13. Employ Staff (Teacher, Housemaster, Kitchen Staff)

| | |
|---|---|
| **Brief Description** | Admin creates staff accounts; the system generates a password and emails the credentials. |
| **Actors** | Admin (primary), new staff member |
| **Triggering Event** | A new staff member joins the school. |
| **Preconditions** | For teachers, classes and subjects exist. |

**Flow of Activities**
1. Admin opens **Settings → Employ Staff** (or the Teachers / Housemasters / Kitchen Staff pages).
2. Admin chooses the role and enters first name, last name, email and phone.
3. For a **Teacher**, Admin also selects at least one class and one subject to teach.
4. System creates the login with the correct role and a generated temporary password.
5. System emails the credentials to the staff member, writes an audit entry and shows the temporary password to the admin.
6. Admin can later **edit** details or **deactivate** the staff member.

**Alternative Flows**
- 3a. Teacher with no class or subject → "Assign at least one class and subject for a teacher."
- 4a. Email already in use → the error is shown.

---

### UC14. Manage School Settings

| | |
|---|---|
| **Brief Description** | Admin maintains the reference data the rest of the system uses. |
| **Actors** | Admin |
| **Triggering Event** | Start of year, or a change in school structure or fees. |
| **Preconditions** | None. |

**Flow of Activities** (Admin → **Settings**)
1. **Grades** — add / edit / delete (name, level).
2. **Classes** — add / edit / delete (grade, section, capacity).
3. **Subjects** — add / edit / deactivate.
4. **Fee types** — set the Registration and Accommodation fee amounts.
5. **Email templates** — edit the subject and body of system emails.
6. **Boarding settings** — weekly meal budget, maximum pre-orders per meal, and kitchen shift times (start and serve time for breakfast, lunch and dinner).
7. **Email test** — send a test email to check the mail settings.

**Business Rules**
- Each kitchen shift must start before its meal is served; times must be in HH:mm format.

---

### UC15. View Audit Logs

| | |
|---|---|
| **Brief Description** | Admin reviews a record of important actions in the system. |
| **Actors** | Admin |
| **Triggering Event** | Investigating a change or a dispute. |
| **Preconditions** | None. |

**Flow of Activities**
1. Admin opens **Audit Logs**.
2. System lists entries 50 per page, newest first (user, action, entity, details, IP, time).
3. Admin filters by user or action.

**Actions that are logged:** application submitted/approved/rejected, learner enrolled, bed allocated/released, manual payment, proof generated, teacher registered, admin mark edit, timetable slot changes, question paper approve/reject/release, coach assigned, achievement added, bus boarding confirmed, and others.

---

# D. Academics

### UC16. Build the Class Timetable

| | |
|---|---|
| **Brief Description** | Admin builds each class's weekly timetable by placing subjects and teachers into periods. |
| **Actors** | Admin |
| **Triggering Event** | A new term starts. |
| **Preconditions** | Classes, subjects and teachers exist. |

**Flow of Activities**
1. Admin opens **Academics → Timetable**, selects a class, term and year.
2. Admin clicks an empty period and selects a subject and teacher.
3. System checks the rules and saves the slot instantly, without reloading the page.
4. Admin can remove a slot or set a colour for each subject.
5. System writes an audit entry for every change.

**Business Rules**
- Monday to Friday, periods 1–7; **period 7 is not available on Friday**.
- A teacher cannot be in two classes in the same period: "This teacher is already scheduled for {class} at this time."

---

### UC17. View & Download Timetable

| | |
|---|---|
| **Brief Description** | Teachers, learners and parents view the relevant timetable and download it as a PDF. |
| **Actors** | Teacher, Learner, Parent, Admin |
| **Triggering Event** | User wants to see their schedule. |
| **Preconditions** | Timetable slots exist for the term. |

**Flow of Activities**
1. Teacher opens **My Timetable** → their personal weekly schedule across classes.
2. Learner opens **Timetable** / Parent opens it from the learner overview → the class timetable.
3. User selects term and year.
4. User clicks **Download PDF** (class timetable for Learner/Parent/Admin; teacher timetable for Teacher/Admin).

---

### UC18. Record Class Attendance

| | |
|---|---|
| **Brief Description** | A teacher takes the daily register for a class. |
| **Actors** | Teacher (primary), Admin, Parent, Learner (view) |
| **Triggering Event** | Start of the school day / lesson. |
| **Preconditions** | Teacher is assigned to the class; learners are enrolled in it. |

**Flow of Activities**
1. Teacher's dashboard shows how many classes still need today's register.
2. Teacher opens **My Classes → Record Attendance** for a class (today, or another date).
3. System lists enrolled learners, defaulting to **Present** (or the saved status if already taken).
4. Teacher marks each learner **Present / Absent / Late** and adds notes.
5. Teacher clicks **Save**; system stores the register (re-saving updates it).
6. Teacher can view **Attendance History** for a date range; Admin can run the **Attendance Report** for any class.
7. Parents and learners see attendance % and day-by-day history.

**Alternative Flows**
- 2a. Teacher not assigned to the class → access is refused.

---

### UC19. Create Assessment

| | |
|---|---|
| **Brief Description** | A teacher sets up a test, assignment or exam for a class and subject. |
| **Actors** | Teacher (primary), Learners & Parents (notified) |
| **Triggering Event** | An assessment is planned. |
| **Preconditions** | Teacher is assigned to the class and subject. |

**Flow of Activities**
1. Teacher opens **Assessments → Create** (list can be filtered by term and type).
2. Teacher enters name, type, class + subject (from their assignments), date, term and total marks.
3. System saves the assessment and **automatically adds it to the school calendar**.
4. System emails the learners in that class (those with logins) and their parents about the assessment.
5. System opens the Capture Marks screen.

**Alternative Flows**
- Teacher can edit the assessment later unless its marks are locked; the calendar entry is updated to match.

---

### UC20. Capture & Submit Marks

| | |
|---|---|
| **Brief Description** | A teacher enters marks; the system calculates percentage, grade and pass/fail; the teacher then locks them. |
| **Actors** | Teacher (primary), Admin |
| **Triggering Event** | An assessment has been written and marked. |
| **Preconditions** | Assessment exists and is not locked. |

**Flow of Activities**
1. Teacher opens **Capture Marks** for the assessment.
2. System lists enrolled learners with any saved marks.
3. Teacher enters marks obtained and comments, then clicks **Save** (can save many times).
4. System calculates the **percentage**, **grade** and **pass/fail** for each learner.
5. When final, teacher clicks **Submit Marks**; system **locks** the marks.

**Business Rules**
- Grade scale: A ≥ 80, B ≥ 70, C ≥ 60, D ≥ 50, E ≥ 40, F ≥ 30, G < 30. **Pass mark is 40%.**
- Locked marks cannot be changed by the teacher: "Marks are locked. Contact Admin to make changes."

---

### UC21. Class Performance & Admin Mark Correction

| | |
|---|---|
| **Brief Description** | Teachers and Admin view class results; Admin can correct a locked mark with a reason. |
| **Actors** | Teacher, Admin |
| **Triggering Event** | End of term review, or a mark query. |
| **Preconditions** | Marks exist. |

**Flow of Activities**
1. Teacher opens **Class Performance** (class + subject + term) for their own classes; Admin opens **Academic Reports** for any class and subject.
2. System shows each learner's assessment percentages, average, final grade and pass/fail, plus class pass/fail counts.
3. Admin clicks **Edit** on a mark, enters the corrected mark, comment and a **reason**.
4. System recalculates the percentage and grade, keeps the mark locked and records the change and reason in the audit log.

---

### UC22. View Academic Report (Parent / Learner)

| | |
|---|---|
| **Brief Description** | Parent or learner views a term summary of results and attendance. |
| **Actors** | Parent, Learner |
| **Triggering Event** | User wants to check progress. |
| **Preconditions** | Learner has an active enrolment. |

**Flow of Activities**
1. User opens **Academic Report** and selects Term 1–4.
2. System shows the attendance summary and **subject averages with final grades** (individual raw marks are not shown).

**Alternative Flows**
- 1a. No active enrolment → "No active enrollment found for this learner."

---

### UC23. Question Paper Bank

| | |
|---|---|
| **Brief Description** | Teachers upload past papers; Admin approves and releases them; users browse and download released papers. |
| **Actors** | Teacher (upload), Admin (review), Teacher / Learner / Parent (browse & download) |
| **Triggering Event** | A teacher has a paper to share. |
| **Preconditions** | Subjects and grades exist. |

**Flow of Activities**
1. Teacher opens **Question Papers → Upload**, enters title, subject, grade, term, academic year and paper type, and attaches the file.
2. System checks the file and saves it as **Pending**: "Awaiting Admin approval."
3. Admin opens **Question Papers** (filter by status), views the paper, and clicks **Approve** or **Reject** (reason required).
4. Admin clicks **Release** on an approved paper; system marks it released and writes an audit entry.
5. Users open **Question Papers**, filter by subject, grade and term, and **Download**.

**Business Rules**
- PDF or DOCX only; maximum 3 MB.
- Status flow: Pending → Approved → Released (or Rejected). Only Pending papers can be approved; only Approved papers can be released; a released paper cannot be rejected.
- Non-admins can only see and download **released** papers.
- Papers expire 3 years after upload; a background job removes expired papers.

---

# E. Calendar & School Events

### UC24. Manage School Calendar

| | |
|---|---|
| **Brief Description** | Admin publishes holidays, announcements, notices and events; everyone sees the calendar relevant to them. |
| **Actors** | Admin (primary), all signed-in users (view, notified) |
| **Triggering Event** | A date needs to be communicated. |
| **Preconditions** | None. |

**Flow of Activities**
1. User opens **Calendar**; system shows the month with colour-coded events.
2. Admin clicks a day and enters title, description, start/end date, type (**Holiday, Announcement, Notice, School Event**) and optionally a grade or class.
3. System saves the event and emails all active teachers, parents and learners (with real email addresses).
4. Admin can edit or delete events. Deleting a School Event also cancels its event-management record.

**Business Rules**
- **Assessment** events are created automatically from UC19 and cannot be edited or deleted on the calendar.
- Admin sees all events; teachers, parents and learners see school-wide events plus those for their grade/class.

---

### UC25. Plan & Publish a School Event

| | |
|---|---|
| **Brief Description** | Admin adds logistics to a calendar School Event, assigns coordinators and publishes it. |
| **Actors** | Admin (primary), Event Coordinators, all users (notified) |
| **Triggering Event** | A school event (e.g. sports day, prize-giving) is planned. |
| **Preconditions** | A calendar event of type School Event exists. |

**Flow of Activities**
1. Admin opens the event from the calendar and clicks **Manage**.
2. Admin enters venue, budget, maximum participants, gate ticket price and notes.
3. Admin assigns coordinators (teachers or learners with logins) and gives each a role; new roles can be added on the spot.
4. Admin clicks **Save** (event stays **Draft**), then **Publish**.
5. System sets the event to **Published** and emails coordinators (with their role, venue, budget and capacity), teachers, parents and learners.

---

### UC26. RSVP & Receive E-Ticket

| | |
|---|---|
| **Brief Description** | A user books a place at a published event and receives a QR e-ticket. |
| **Actors** | Parent, Teacher (and other signed-in users) |
| **Triggering Event** | User wants to attend an event. |
| **Preconditions** | Event is Published and not full. |

**Flow of Activities**
1. User opens the event on the calendar and clicks **RSVP**.
2. System checks the event is open and has space.
3. System creates a confirmed RSVP and a unique ticket, and emails a confirmation.
4. User opens **My Ticket** to show the QR code at the entrance.
5. User can **Cancel RSVP**; the ticket is cancelled.

**Alternative Flows**
- 2a. Event not published → "Event is not open for RSVP."
- 2b. Capacity reached → "Event is fully booked."

---

### UC27. Scan Tickets at the Event

| | |
|---|---|
| **Brief Description** | Admin or a coordinator scans e-tickets and gate tickets at the entrance. |
| **Actors** | Admin, Event Coordinator |
| **Triggering Event** | Guests arrive at the event. |
| **Preconditions** | User is Admin or a coordinator of this event. |

**Flow of Activities**
1. Scanner opens **Scanner** for the event on a phone or tablet (camera QR scanner).
2. Scanner scans a ticket QR code.
3. System responds:
   - **Valid** → ticket marked used, entry recorded.
   - **Already used** → shows when it was used.
   - **Invalid / cancelled** → entry refused.
4. **Walk-ins:** Admin displays the **Gate QR**; each scan of it records one paid gate entry.
5. Every scan is logged with time and device.

---

### UC28. Event Summary

| | |
|---|---|
| **Brief Description** | Admin reviews how the event went. |
| **Actors** | Admin |
| **Triggering Event** | During or after the event. |
| **Preconditions** | Event exists. |

**Flow of Activities**
1. Admin opens **Summary** for the event.
2. System shows: budget vs actual spend, capacity, RSVPs, tickets scanned, gate tickets issued/scanned, attendance rate, coordinators and a scan timeline.

---

# F. Transport

### UC29. Manage Buses & Drivers

| | |
|---|---|
| **Brief Description** | Admin keeps the fleet and driver records up to date. |
| **Actors** | Admin (primary), Teacher (view drivers) |
| **Triggering Event** | A bus or driver is added, or details change. |
| **Preconditions** | None. |

**Flow of Activities**
1. **Buses:** Admin opens **Transport → Buses**, adds or edits registration number, make/model and capacity, or deactivates a bus.
2. **Drivers:** Admin opens **Transport → Drivers → Add/Edit Driver** and enters full name, ID/passport number, phone, email, address, licence number and expiry date, employment date and shift hours; uploads a photo and licence document.
3. System saves the record. Admin can deactivate a driver.
4. The Transport dashboard shows the number of **licences expiring within 30 days**.

---

### UC30. Schedule Transport

| | |
|---|---|
| **Brief Description** | Admin schedules daily shuttles and one-off trips, and the system notifies parents and teachers. |
| **Actors** | Admin (primary), Parent and Teacher (notified) |
| **Triggering Event** | New term (daily shuttle timetable) or a trip request (sport, cultural). |
| **Preconditions** | Active buses and drivers exist; boarding learners have bed allocations. |

**Flow of Activities**
1. Admin opens **Transport → Schedule Transport**.
2. Admin fills in:
   - Route: **Boarding → School** or **School → Boarding**
   - Purpose: **Daily Shuttle, Sport, Cultural, Other** (+ detail)
   - Departure and arrival time
   - Bus and driver (dropdowns)
   - **Recurring** (select days of the week + optional end date) **or** a single trip date
   - Roster: **all boarding learners** or selected boarding learners
3. Admin clicks **Save**.
4. System creates the trip and emails the parents of the learners on the roster and all active teachers.
5. Trip appears on **Trips** for Admin and Teachers, and for Parents whose child is on it.

---

### UC31. Board the Bus

| | |
|---|---|
| **Brief Description** | Staff mark which learners boarded so no one is left behind; missing learners trigger alerts. |
| **Actors** | Teacher or Admin (primary), Parent (view own child), Housemaster (alerts) |
| **Triggering Event** | Learners begin boarding the bus. |
| **Preconditions** | Trip is scheduled for that date; learners are on the roster. |

**Flow of Activities**
1. Teacher opens **Trips** and clicks **Board the Bus** for the trip (today or a chosen date).
2. System shows the roster with each learner's status.
3. Teacher marks each learner **Boarded / Not boarded / Absent** and sees the count boarded vs expected.
4. Teacher clicks **Confirm Attendance**.
5. System saves the boarding records with time and who marked them, and writes an audit entry.
6. For every learner on the roster who did **not** board, system emails the **Housemasters** and the learner's **parent**.
7. Housemasters see these under **Transport → Missing Learner Alerts** (last 14 days).
8. Parents can open the same screen read-only to see their own child's status.

---

### UC32. Report Bus Delay & Notify Parents

| | |
|---|---|
| **Brief Description** | Staff report a delay and parents are told the new arrival time. |
| **Actors** | Admin or Teacher (primary), Parent (notified) |
| **Triggering Event** | Bus is running late. |
| **Preconditions** | Trip is scheduled for the day; parent emails are on file. |

**Flow of Activities**
1. Teacher/Admin opens the trip and clicks **Report Delay**.
2. Selects a reason: **Traffic, Breakdown, Weather, Accident, Other**, adds notes and the new estimated arrival time.
3. Clicks **Submit**.
4. System logs the delay report and emails the parents of the learners on that bus.

---

### UC33. Monitor Fuel Usage

| | |
|---|---|
| **Brief Description** | Admin records refuels and the system calculates efficiency and reports. |
| **Actors** | Admin |
| **Triggering Event** | A bus is refuelled. |
| **Preconditions** | Fuel slip and odometer reading are available. |

**Flow of Activities**
1. Admin opens **Transport → Fuel Management → Add Fuel Record**.
2. Selects the bus and enters date, litres, total cost, odometer reading and station name.
3. Clicks **Save**.
4. System calculates **km per litre** from the previous record for that bus and saves the record.
5. Admin opens **Fuel Report**, filters by bus and date range, and sees total litres, total cost and average efficiency.
6. Admin can **download the report as PDF**.

---

# G. Extracurricular Activities

### UC34. Schedule Activities

| | |
|---|---|
| **Brief Description** | Admin creates sports, cultural and club activities. |
| **Actors** | Admin (primary), Teacher / Parent / Learner (view) |
| **Triggering Event** | A new activity starts or a new term begins. |
| **Preconditions** | None. |

**Flow of Activities**
1. Admin opens **Extracurricular → Schedule Activity**.
2. Admin enters name, type (**Sport, Cultural, Academic Club, Other**), description, venue, start and end time, maximum participants, grade range (min–max, or all grades) and whether registration is open.
3. Admin clicks **Save**.
4. For a new activity, system emails all active teachers and parents.
5. Activities appear on **Activities** for all roles (filter by type).

---

### UC35. Register a Learner for an Activity

| | |
|---|---|
| **Brief Description** | A parent signs their child up for an activity. |
| **Actors** | Parent |
| **Triggering Event** | Learner wants to join an activity. |
| **Preconditions** | Activity registration is open and not full. |

**Flow of Activities**
1. Parent opens **Activities**, chooses an activity and the learner, and clicks **Register**.
2. System checks registration is open and there is space, and registers the learner (re-registering a withdrawn learner is allowed).

**Alternative Flows**
- 2a. "Registration is closed for this activity." / "This activity is fully booked."

---

### UC36. Assign Coaches / Mentors

| | |
|---|---|
| **Brief Description** | Admin assigns teachers to lead or supervise activities. |
| **Actors** | Admin (primary), Teacher/Coach (notified) |
| **Triggering Event** | New activity, or a coach joins/leaves. |
| **Preconditions** | Teachers and activities exist. |

**Flow of Activities**
1. Admin opens **Extracurricular → Assign Coaches**.
2. Selects an activity and a teacher, a role (**Head Coach, Assistant Coach, Mentor, Supervisor**) and a period (**Term 1–4, Full Year**).
3. Clicks **Assign**.
4. System saves the assignment (or updates an existing one), flags the teacher as a coach and writes an audit entry.
5. System emails the teacher: "You have been assigned as {role} for {activity} ({period})."
6. The teacher now sees **My Coaching** in their menu with their activities.

---

### UC37. Activity Attendance Register

| | |
|---|---|
| **Brief Description** | A coach marks attendance for each activity session. |
| **Actors** | Coach (primary), Parent and Learner (view) |
| **Triggering Event** | An activity session begins. |
| **Preconditions** | Teacher is a coach of the activity; learners are registered. |

**Flow of Activities**
1. Coach opens **My Coaching** and selects the activity (and date, default today).
2. System creates the session if needed and lists registered learners.
3. Coach marks each learner **Present, Absent, Late, Excused** and adds a note (e.g. "Late due to class test").
4. Coach clicks **Submit Attendance**; system saves it with date and time.
5. Parents and learners view the learner's **activity attendance history** for a date range.

**Alternative Flows**
- 1a. Teacher is not a coach of that activity → access is refused.

---

### UC38. Record Achievements

| | |
|---|---|
| **Brief Description** | Admin or a coach records a learner's achievement in an activity. |
| **Actors** | Admin or Coach (primary), Parent (notified), Learner (view) |
| **Triggering Event** | Learner wins, earns a certificate, is made captain, etc. |
| **Preconditions** | Learner and activity exist. |

**Flow of Activities**
1. User opens **Achievements → Add Achievement** (coaches only see their own activities).
2. Selects learner and activity; enters type (**Certificate, Award, Trophy, Position, Other**), description, date achieved and level (**School, District, Provincial, National**); optionally uploads a photo/document.
3. Clicks **Save**.
4. System saves it to the learner's record, writes an audit entry and emails the parent: "Congratulations! Your child received a {type} in {activity}…"
5. Parents and learners see the learner's achievements.

---

### UC39. Activity Report

| | |
|---|---|
| **Brief Description** | Generate a report on participation, attendance, achievements and coaches. |
| **Actors** | Admin (all activities), Coach (own activities) |
| **Triggering Event** | End of term/year or a management request. |
| **Preconditions** | Activities, registrations, attendance or achievements exist. |

**Flow of Activities**
1. User opens **Activity Report** and filters by type, date range (default last 6 months), grade and specific activity.
2. System shows:
   - Activity summary: total activities, total participants, activities by type
   - Attendance summary: % present per activity, average attendance
   - Achievements: totals per activity, top achievers
   - Coach summary: coaches and their activities
   - Participation: learners and how many activities they take part in
3. User can **Download PDF** or open a **Print** view.

---

# H. Boarding Life

### UC40. Issue Boarding QR Badge

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

---

### UC41. Request & Approve Leave

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
- 2a. Return date before departure date → error shown.
- 1a. Learner is not a boarder → "Leave requests are only available for boarding learners."

---

### UC42. Check-Out & Check-In (QR Scan)

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

---

### UC43. Housemaster Dashboard & Movement Log

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

---

# I. Meals & Kitchen

### UC44. Manage Dietary Profile & Allergies

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

---

### UC45. Manage Ingredients & Meal Library

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
- At least one ingredient per meal; each ingredient listed once; ingredient names must be unique; meal image JPG/PNG/WEBP/GIF under 5 MB.

---

### UC46. Create & Publish the Weekly Meal Plan

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

---

### UC47. Pre-Order Meals

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

---

### UC48. Finalise Pre-Orders & Generate Purchase Requisition

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

---

### UC49. Receive Stock

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

---

### UC50. Record Ingredient Usage & Leftovers

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

---

### UC51. Kitchen Teams & Schedule

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

---

### UC52. Update Kitchen Tasks

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

---

### UC53. Meal Attendance (Dining Hall Scanning)

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

---

### UC54. Meal Feedback & Quality Alerts

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

---

### UC55. Meal Compliance Report

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

---

# Appendix

## A. Automatic Email Notifications

| Event | Who is emailed |
|---|---|
| Contact form sent | School office |
| Application received / approved / rejected | Parent |
| Payment completed (online or manual) | Parent |
| Proof of registration generated | Parent (PDF attached) |
| Staff employed | New staff member (credentials) |
| Assessment created | Learners in the class + their parents |
| Calendar event created | All active teachers, parents and learners |
| School event published | Coordinators, teachers, parents, learners |
| Event RSVP | The person who booked |
| Transport scheduled | Parents of learners on the roster + teachers |
| Learner did not board bus | Housemasters + parent |
| Bus delay reported | Parents of learners on the bus |
| New activity created | Teachers + parents |
| Coach assigned | The teacher |
| Achievement recorded | Parent |
| Leave request submitted | Housemasters |
| Leave approved / rejected | Parent |
| Learner checked out / checked in late | Housemasters + parent |
| Dietary profile saved | Kitchen staff + housemasters (+ parent if the learner added it) |
| Allergy conflict with published meals | Kitchen staff + housemasters |
| Kitchen schedule published | Assigned kitchen staff |
| Kitchen task delayed | Admins |
| 3 consecutive missed meals | Housemasters |
| Meal average below 2.5 | Kitchen staff + housemasters |

## B. Differences from the Original Use Case Specification

These are the points where the system **as built** differs from the first version of this document (and the boarding build notes). They are listed so they can be confirmed as intended or added to the backlog.

| # | Original description | What the system does now |
|---|---|---|
| 1 | Board the bus: **Teacher or Housemaster** marks learners | Only **Teacher or Admin** can mark; Housemasters get missing-learner alerts instead |
| 2 | Schedule transport publishes to learners, parents, teachers and housemasters | Emails **parents and teachers**; housemasters see trips on the Transport dashboard |
| 3 | Driver details: system **sends an alert** when documents expire | Dashboard shows a **count of licences expiring within 30 days**; no email is sent |
| 4 | Driver details live under "Staff Management" | They live under **Transport → Drivers** |
| 5 | Schedule activities publishes to **learners and teachers**; learners register themselves | Emails **teachers and parents**; **parents** register learners |
| 6 | Achievement notifies **learner and parent** | Notifies the **parent** only |
| 7 | Activity report exports **PDF, Excel, Print** and can be shared with management | **PDF and Print** only; no Excel or sharing |
| 8 | Activity has a generated unique "Activity ID" | Uses the normal database ID |
| 9 | Dietary profiles are **reviewed and approved** by kitchen staff | Profiles are **active immediately**; kitchen staff and housemasters are notified and can view them |
| 10 | Wait-listed pre-orders are notified when a spot opens | Cancelling an order does **not** promote or notify wait-listed learners |
| 11 | Kitchen staff can **manually mark a learner present** if the scanner fails | The manual override exists in the code but has **no screen** yet |
| 12 | Meal compliance report generated by **Housemaster or Admin** | Admin's menu links to it, but the page is restricted to **Housemasters**, so Admin gets "Access Denied" (likely a bug) |
| 13 | Leave needs **parent and housemaster** approval | Parent submitting the request counts as the parent's approval; only the housemaster approves |
