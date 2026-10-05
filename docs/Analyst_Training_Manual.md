# Cat Management System
## Analyst Training Manual

**Audience:** CAT analysts (Analyst I, Analyst II, and Analytics Leads)  
**Build:** 1.2.0  
**Date:** 24 September 2026

This manual is for CAT analysts who use the Cat Management System (CMS) to manage client projects, keep the analytics calendar current, and enter time. Confirm you are on **Build 1.2.0** — the same label appears at the bottom of the hub Home screen (**Select Application**) and on the Client Management Home page. Follow the steps in order the first time you train, then use Section 10 as a day-to-day checklist. Changes since the previous build are in Appendix B.

---

## Table of contents

1. Getting started
   - 1.1 What this application is for
   - 1.2 Sign in and confirm the build
   - 1.3 Choosing an application (CM, TM, and CatAI)
   - 1.4 How to move around
   - 1.5 Your role on a project
2. Finding your work (Home)
   - 2.1 Summary tiles
   - 2.2 The Client / Project list
   - 2.3 Filter, sort, and search
   - 2.4 Open a project to view or edit
   - 2.5 Generate a report from the list
3. Clients
   - 3.1 View client details from a project
   - 3.2 Create or edit a client
4. Projects — add, view, and edit
   - 4.1 Create a new project
   - 4.2 Project Info
   - 4.3 Client Info tab
   - 4.4 Services
   - 4.5 Project Data
   - 4.6 Analysis Profile
   - 4.7 Revenue history
   - 4.8 Notes
   - 4.9 Save, cancel, and restore changes
   - 4.10 Project Summary
5. Calendar and team communications
   - 5.1 Add, edit, and complete calendar items
   - 5.2 Assign owners from the user group
   - 5.3 Automatic owner emails
   - 5.4 Notify Team
   - 5.5 Open a project from an email link
6. Entering time
   - 6.1 Weekly Time Management
   - 6.2 Client Time
   - 6.3 Non-Client Time
   - 6.4 Save time
   - 6.5 Project Time tracker
   - 6.6 TM Dashboard
7. Reports and dashboards
   - 7.1 Generate a project report
   - 7.2 Generate Report & Send
   - 7.3 PM Dashboard
   - 7.4 Drill from a chart into a project
8. CatAI (live)
9. What not to do
10. Quick reference
    - 10.1 Glossary
    - 10.2 Common tasks checklist
Appendix A. Technologies used
Appendix B. What’s new in this build (1.2.0 vs 1.1)

---

## 1. Getting started

### 1.1 What this application is for

The Cat Management System is the CAT team’s workspace for client and project work. As an analyst, you will use it to:

- Find the clients and projects you are assigned to
- Keep project information, services, data files, and notes up to date
- Maintain the analytics calendar (deliverables, meetings, and tasks)
- Enter your hours against calendar tasks and non-client activities
- Generate project reports and send updates to the project user group

The application has three tiles on the hub. CM and TM are the working applications; CatAI is the chat assistant (Section 8).

| Tile | Full name | What you do there |
| --- | --- | --- |
| **CM** | Client Management | Clients, projects, calendar, files, notes, and reports |
| **TM** | Time Management | Weekly time entry and utilization reporting |
| **CatAI** | CatAI | Ask questions about clients, projects, and work in natural language |

You are not expected to set up users, user groups, or system reference data. Those are Admin tasks (see Section 9).

### 1.2 Sign in and confirm the build

Access is limited to **active CAT department users**. Until you sign in, you cannot open the hub, CM, TM, or CatAI.

**First launch**

1. Open the application. You will see **Sign in to launch**.
2. Choose your **Username (email)** from the list. First-time accounts show **(first launch)**.
3. Enter **Create password** and **Confirm password**.
4. Click **Set password & launch**.

There is no default password until you create one on first launch. There is no “forgot password” on this screen — ask an administrator if you are locked out.

**Returning users**

1. Choose your **Username (email)**.
2. Enter **Password**.
3. Click **Launch**.

A welcome message with your name confirms you are in. Your session lasts about 12 hours of activity.

**Confirm you are on this manual’s build**

After sign-in you land on the hub Home screen (**Select Application**). At the bottom, under **Get Help**, the application shows **Build 1.2.0**. The Client Management Home page (after **Open CM**) shows the same **Build 1.2.0** next to **Send Feedback**.

If the number does not match this document, you are on a different build — use **Get Help** for the PDF that belongs to the running application, or ask for the matching manual.

**Sign out**

On the hub, your name appears at the top right with **Sign out**. Sign out when you leave a shared machine.

**Who else is signed in**

Stacked avatars at the top of the screen show other people currently in CMS. Click the stack to open **Also in CMS**. This appears on every page after sign-in.

### 1.3 Choosing an application (CM, TM, and CatAI)

When you open the application (after sign-in) you land on the **Select Application** hub. The heading is **Cat Management System**.

You will see three tiles:

**Client Management (CM)**  
*Clients, projects, services, reports, and advisory workflows.*  
Click **Open CM**.

Use CM when you need to look up a project, update calendar items, upload data, add notes, or generate a report.

**Time Management (TM)**  
*Time tracking and allocation across projects and analysts.*  
Click **Open TM**.

Use TM when you need to enter or review weekly hours. Time is entered by **CAT user** and by week. Client-project hours in TM come from the same calendar tasks you maintain in CM.

**CatAI**  
*Ask questions about clients, projects, and work in natural language.*  
Click **Open CatAI**.

Use this tile to open the CatAI assistant (Section 8). Treat answers as a starting point and confirm anything that affects a deliverable, calendar owner, or time entry in the project itself.

Under the tiles:

- **Get Help** opens this training manual (PDF).
- **Build 1.2.0** is the application build. It must match the **Build** on the cover of this document.

**How they connect**

- The **user group** on a project determines who is on the team and which projects appear in TM.
- **Calendar items** on a project become the task rows you charge time against.
- Setting a project **online** or **offline** in CM changes how that project is grouped in TM time entry.
- Hours saved in TM and hours saved on the project **Time tracker** tab are the same time records. You can enter them in either place.

**Switching between applications**

1. Click **Home** in the left sidebar (or the **Cat** logo at the top of the sidebar).
2. You return to the Select Application hub.
3. Choose **Open CM**, **Open TM**, or **Open CatAI**.

You can also go directly to TM from the hub, or stay in CM and open a project from the project list. The sidebar remembers whether you last used CM or TM and shows the matching menu. CatAI is opened from the hub tile, not from the CM sidebar.

### 1.4 How to move around

The left sidebar is always visible. The **Cat** logo and **Cat Mgmt** label at the top return you to the hub.

The rest of the menu depends on whether you are on the hub, in CM, or in TM.

#### On the hub (Select Application) or in CatAI

| Sidebar item | What it does |
| --- | --- |
| **Home** | Stays on (or returns to) the Select Application hub |
| **Admin** | Opens the Admin console — not typical analyst work |

CatAI does not have its own sidebar items. Open it from the hub **Open CatAI** tile, then use **Home** to return to the hub.

#### In Client Management

| Sidebar item | What it does |
| --- | --- |
| **Home** | Returns to the Select Application hub |
| **Project List** | Opens the Client Management Home grid (same as **Open CM**) |
| **New Project** | Starts a new project |
| **Dashboard** | Opens the project portfolio and client-health dashboard |
| **Admin** | Opens the Admin console — not typical analyst work |

After you choose **Open CM**, the main page is titled **Client Management System**. At the top you will see summary tiles (active projects, revenue, online projects). Below that is the **Client / Project list**, which is the usual place to find and open your work (Section 2). **Build 1.2.0** appears next to **Send Feedback**.

#### In Time Management

| Sidebar item | What it does |
| --- | --- |
| **Home** | Returns to the Select Application hub |
| **Dashboard** | Opens the TM utilization and hours dashboard |
| **Admin** | Opens the Admin console — not typical analyst work |

After you choose **Open TM**, the main page is weekly time entry. Time entry is locked to **your signed-in CAT account**. Choose a week and enter hours on **Client Time** or **Non-Client Time** (Section 6). Administrators can still pick another CAT user.

**Tips**

- **Home** always means the application hub, not the CM project list. To get back to the project list, choose **Home**, then **Open CM**, or click **Project List** while you are in CM.
- **Dashboard** is a different screen in each workspace: project analytics in CM, time and utilization in TM.
- **CatAI** is a hub tile (**Open CatAI**), not a CM menu item.
- Confirm **Build 1.2.0** on the hub before you train from this document.

### 1.5 Your role on a project

You sign in as a **CAT department user**. Your project work is then defined by the **user groups** you belong to and the **project role** on each group.

#### User groups

A **user group** is the project team. Each project is tied to one user group. Members of that group:

- Appear on the project (and on project reports)
- Can be chosen as **owners** of calendar items
- Receive **Notify Team** emails for that project’s calendar
- See that project under **Client Time** in TM when they enter hours

If you cannot find a project in TM, you are usually missing from that project’s user group. Ask a lead or administrator to add you.

#### Project roles

Roles describe your job on that team, not a login permission. The roles you will see most often are:

| Role | Typical meaning |
| --- | --- |
| **Analytics Lead** | Owns analytics for the client; often the name shown in Home filters and dashboards |
| **Analyst I** | Lead analyst on the project |
| **Analyst II** | Support analyst on the project |
| **Account Executive** | Relationship manager (client-facing, not CAT delivery) |
| **Broker / Broker II / Broker III** | Broker team members on the account |
| **Manager** | Management member of the user group |

You may be Analyst I on one project and Analyst II on another. The same person can also be Analytics Lead on some accounts.

#### What this means for your daily work

As **Analyst I** or **Analyst II** you should be able to:

- Open your projects from the CM Home list
- Update calendar items, including marking work complete and assigning yourself as owner
- Notify the user group when the calendar is ready
- Upload and download project data files
- Add notes
- Enter your weekly time in TM (and on the project Time tracker if you are working in the project)
- Generate a project report when asked

**Analytics Leads** typically also keep the user group, project status, online/offline flag, services, and revenue current, and they watch the dashboards for incomplete time and project health.

Other roles (Account Executive, Broker, Manager) appear on the team list and in reports. They are not the audience for this manual.

---

## 2. Finding your work (Home)

Open **Client Management** from the hub. The Home page is titled **Client Management System**.

The subtitle reads: *Manage clients, projects, advisory services, administrative data, and dashboard reporting.*

A **Send Feedback** link is available on this page if you need to report an issue or suggestion.

### 2.1 Summary tiles

Four tiles sit above the list. They follow the filters you apply:

| Tile | What it shows |
| --- | --- |
| **Active Projects** | Count of active projects in the current filter |
| **Total Revenue (USD)** | Revenue for those active projects |
| **Online Projects** | Count of projects currently marked Online |
| **Online Revenue** | Revenue for online projects |

Use these tiles as a quick health check before you open the grid. If Online Projects looks too low, confirm the **Online** filter (Section 2.3).

### 2.2 The Client / Project list

The grid is titled **Client / Project list**. Each row is a client/project combination. Columns include client and project identifiers, status, online flag, and a column for each project role (for example **Analytics Lead** and **Analyst I**) showing who is assigned.

**How to use the grid**

- **Click a row** to select it. Selected rows can be opened with **Edit**.
- **Double-click a row** to open or create a project for that client.
- **Right-click a row** for report options (Section 2.5).
- Click a **column header** to sort.

Do not use **Bulk import** or **Delete** on this page unless a lead or administrator has asked you to. Those actions are covered in Section 9.

### 2.3 Filter, sort, and search

Filters sit above the grid:

| Filter | Typical use |
| --- | --- |
| **Business Unit** | Limit to one business unit |
| **Business Class** | Limit to one class |
| **Status** | Limit by project status |
| **Online** | **Online**, **Offline**, or **All** |
| **Analytics Lead** | Show work owned by a specific lead (including yourself) |
| **Client** | Jump to one client |
| **Project Type** | Limit by project type |

**Default view:** the list opens to **Online** projects for all clients. That is the working set you will use most days.

**Buttons**

- **Apply filters** — refresh the list with the current filter values
- **Clear all** — reset to all clients with **Online** status Online
- **Refresh table** — reload the list from the database
- **Edit** — open the selected row (see 2.4)
- **Delete** — permanent hard delete; do not use this as an analyst

Your last filter choices are remembered for the session, so returning to Home usually keeps the view you had.

### 2.4 Open a project to view or edit

**To open an existing project**

1. Filter until you can see the row.
2. Select the row and click **Edit**, or **double-click** the row.
3. In the **Open or create project** window:
   - Click **Open existing project** to edit that project.
   - Click **Create new project** only if you intend to start a new project for that client.
   - Click **Cancel** to stay on Home.

**If the row has no project yet**

The dialog explains that no project is on file for that client on this row (for example, there may be no project or broker assignment yet). Choose **Create new project** only when you are supposed to set one up.

**From the sidebar**

Click **New Project** to start a blank project instead of using a Home row.

### 2.5 Generate a report from the list

1. Right-click a project row.
2. Choose **Generate Report** to create a PDF, or **Generate Report & Send** to create a PDF and open an Outlook draft with the file attached.

Wait for **Generating…** to finish. Report contents and sending are described in Section 7.

You can also generate a report from inside the project (every project tab has **Generate Report**).

---

## 3. Clients

Most of the time you will see client information on the project **Client Info** tab. You only need the standalone client pages when a new client must be created or an existing client’s classification needs a correction.

### 3.1 View client details from a project

1. Open the project.
2. Open the **Client Info** tab.

You will see:

- **Client** (locked on an existing project — the project stays tied to that client)
- **Client name** and **Short name** (read-only)
- **Business Unit**, **Business Class**, **Client Type**, and **Status** (editable dropdowns)

On an existing project you can update business unit, class, client type, and status from those dropdowns, then click **Save** on this tab.

Client name itself is not renamed here. If the legal name is wrong, edit the client record (Section 3.2) or ask Admin.

### 3.2 Create or edit a client

**When to create a client:** only when the client does not already exist and you have been asked to set up a new account. Duplicate clients cause duplicate rows on Home.

**To create a client**

1. Go to `/clients/new` (or ask a lead to open **New Client**).
2. Complete:
   - **Client Name** (required)
   - **Client Short Name**
   - **Business Unit**
   - **Business Class**
   - **Client Category**
   - **Client Type**
   - **Status** (Active is the usual choice; Prospect is also allowed)
3. Click **Save Client**.

A directory of active clients appears on the left so you can search and open an existing client instead of creating a duplicate.

**To edit a client**

1. Open the client from the directory (or from a known client ID).
2. Change the fields that need correction.
3. Click **Save Client**.

**Do not** use **Soft-delete** or **Restore Client** unless a lead or administrator has instructed you to. Soft-delete hides the client from active lists; it is not a routine analyst action.

---

## 4. Projects — add, view, and edit

A project is the working record for CAT delivery. The page title is **New Project** or **Edit Project**. Work is organized in tabs.

| Tab | Available | What it is for |
| --- | --- | --- |
| **Client Info** | New and edit | Who the client is and how they are classified |
| **Project Info** | New and edit | Number, name, user group, dates, status, online flag, revenue |
| **Project Data** | New and edit | Analysis files (upload, download, email) |
| **Services** | New and edit | Service catalog; add selected services to the calendar |
| **Analysis Profile** | Edit only | Moody’s / Verisk BAR settings |
| **Calendar** | New and edit | Deliverables, meetings, tasks, owners, dates |
| **Time tracker** | Edit only | Weekly hours for this project |
| **Revenue history** | Edit only | Revenue changes over time |
| **Notes** | New and edit | Project notes |

On a **new** project, click **Save Project** at the bottom to create the record (files and notes queued on the form are saved at the same time). After the project exists, each tab has its own **Save** and **Cancel**.

### 4.1 Create a new project

1. From CM, click **New Project** in the sidebar, or use **Create new project** from a Home row.
2. On **Client Info**, search and select an **active** or **prospect** client. Only those statuses appear in the list.
3. On **Project Info**, complete at least:
   - **Project Number**
   - **Project Name**
   - **User Group** (the project team — required before calendar owners or Notify Team will work)
   - **Project Type**
   - **Status**
   - **Renewal Date** and **Due Date** if known
   - Initial **Revenue**, **Revenue Effective Date**, and **Revenue Change Reason** if you are recording revenue at create
4. Optionally add services, calendar items, files, and notes before the first save.
5. Click **Save Project**.

If you skip the user group, you can still save the project, but you will not be able to assign calendar owners or notify the team until a group is selected.

### 4.2 Project Info

Open **Project Info** to maintain the header of the project.

| Field | Notes |
| --- | --- |
| **Project Number** | Required identifier |
| **Online** checkbox | **Online (in progress)** or **Offline**. On an existing project this saves immediately when you toggle it. Online projects appear in the Online group in TM. |
| **Project Name** | Display name used throughout CM and TM |
| **User Group** | Team assignment. Changing the group clears calendar owners that no longer apply. There is a link to Admin user groups if a lead needs to create or edit the group. |
| **Project Type** | Type of work |
| **Renewal Date** / **Due Date** | Planning dates used on Home and dashboards |
| **Status** | Project status |
| **Revenue** fields | Latest revenue is shown here. Saving a new amount appends a history row (see 4.7). |

The green or grey **online dot** next to the checkbox matches the dot on Home and in TM.

**Soft-delete / Restore** sit in the footer danger zone. Do not use them unless instructed (Section 9).

### 4.3 Client Info tab

See Section 3.1. On a **new** project you pick the client here. On an **existing** project the client cannot be switched; you can still update classification fields and **Save**.

### 4.4 Services

The **Services** tab lists the service catalog by **Service Category** and **Service**.

1. Filter or scroll the catalog.
2. Check the services this project will deliver. Use **Select all services** or **Clear all** if needed.
3. Click **Add to Project**.

The app creates matching **Calendar** rows for the selected services, switches you to the **Calendar** tab, and shows a message such as *N services added to the Calendar. Save the project to persist.*

**Save the Calendar tab** (or **Save Project** if this is still a new project) or the new rows will be lost.

Adding services to the calendar is the usual way to build the first task list you will charge time against.

### 4.5 Project Data

Use **Project Data** for the files that support the analysis (not the project report PDF).

Classifications:

- **Source Data**
- **Exposure Data**
- **Loss Data**
- **Claims Data**
- **Correspondence**
- **Submission**

**To upload**

1. Click **Add Data**.
2. Choose the file.
3. Set **Classification**, **Version** (1–20), **Data As Of** (required), and a description if useful.
4. Mark the file **current** / process-active when it is the version that should be used.
5. Save. On a new project, files are queued until you click **Save Project**.

**To work with an existing file**

- Click a row to select it.
- **Double-click** to edit classification, version, data-as-of, description, or current flag.
- **Download file** opens the stored file.
- **Email file** creates an Outlook `.eml` draft with the file attached.

Keep versions honest and **Data As Of** accurate. Downstream analysis profile prefill can use the active file.

### 4.6 Analysis Profile

**Analysis Profile** appears only after the project is saved. It holds Moody’s (AIR) and Verisk (RMS) BAR configuration for the project.

1. Open **Analysis Profile**.
2. Choose the **Moody’s** or **Verisk** vendor tab.
3. Complete the analysis settings required for the run.
4. Prefill from the active project data file when that option is available and the file metadata is correct.
5. **Save** this tab.

This tab is specialist work. If you are not configuring a model run, you can skip it. Ask your Analytics Lead before changing a profile that is already in use.

### 4.7 Revenue history

**Revenue history** (edit only) lists each revenue row with **effective date**, **amount**, **change reason**, and who recorded it.

- View history to understand how the project’s revenue has changed.
- Add a new amount from **Project Info** (latest revenue) or from this tab, with an effective date and reason.
- Delete a row only if a lead has confirmed it was entered in error.

Most analysts **read** this tab. Analytics Leads usually own the updates.

### 4.8 Notes

Use **Notes** for working comments that should travel with the project (handoffs, data issues, client instructions).

1. Add the note text (and type / pin if you use those fields).
2. **Save** the Notes tab.
3. On a **new** project, notes are queued until **Save Project**. Pending notes show as *(pending save)*.

Delete a note only when it is obsolete or was entered on the wrong project.

### 4.9 Save, cancel, and restore changes

**New project:** one **Save Project** button creates the project and writes queued files, calendar items, and notes.

**Existing project:** each tab has:

- **Save** — writes that tab
- **Cancel** — reverts unsaved edits on that tab
- **Unsaved changes** hint when the tab is dirty

The **Online** checkbox on Project Info is the exception: it saves as soon as you toggle it.

If you navigate away with unsaved tab changes, you can lose work. Save the tab you are on before you switch to TM or another project.

Footer actions also include soft-delete and restore. Leave those to a lead or administrator.

### 4.10 Project Summary

The **Project Summary** page is a read-only snapshot: header, services, and revenue history. Use it when you want to review without risk of editing.

From summary, use the edit control to open the full **Edit Project** form.

---

## 5. Calendar and team communications

The **Calendar** tab is the analytics calendar for the project. It is also the task list used for time entry. Keep it current.

Item types:

- **Deliverable**
- **Meeting**
- **Task** (default for a new row)
- **Other**

Each row has **Title**, **Owner**, **Received** date, **Completed** date, **Notes**, and sort order.

### 5.1 Add, edit, and complete calendar items

**Add a row**

1. Open **Calendar**.
2. Add a row (or use **Add to Project** from Services — Section 4.4).
3. Set **Type** and **Title** (title is required).
4. Set **Received** when work or data arrived.
5. Set **Completed** when the item is done. Leave it blank while work is in progress.
6. Add **Notes** if the owner or the next person will need context.
7. Click **Save** on the Calendar tab.

**Edit a row**

- Change fields directly on the grid, or open **Edit calendar item**.
- In the edit window, click **Apply**, then **Save** on the Calendar tab. Apply alone does not persist.

**Remove a row** only if it was added in error. Do not delete a task that already has time charged to it without checking with your lead.

Completed items should keep their title and owner so TM history and reports still make sense.

### 5.2 Assign owners from the user group

**Owner** lists members of the project’s **User Group**.

1. Confirm **User Group** is set on **Project Info**. If it is not, Notify Team is disabled and owners cannot be chosen.
2. On each calendar row, choose an owner from **-- Owner --**.
3. Assign yourself on work you own; assign teammates on work they own.
4. **Save**.

If the user group changes, owners that are no longer in the group are cleared. Re-assign them after the new group is in place.

### 5.3 Automatic owner emails

After you **Save** the calendar, the system may send a summary email **automatically** when:

- Every active calendar item has an owner, and
- Owner assignments are new or have changed since the last save

The email goes to the owners’ addresses, with a CC as configured for calendar notifications. The subject looks like:

*Analytics calendar tasks assigned – {Project Number} {Project Name}*

The body lists the tasks and includes a link to the project calendar.

**This is not the same as Notify Team.** Automatic mail fires when ownership is complete. Notify Team is the manual “please review the calendar” message you send when you want the whole user group to look.

If some rows still have no owner, the automatic email is not sent. Finish assigning owners, then save again.

### 5.4 Notify Team

Use **Notify Team** when the calendar is ready for the user group to review — for example after you have built the first calendar, after a major update, or when you need people to confirm owners and dates.

1. Confirm the project has a **User Group** and that members have email addresses.
2. **Save** the Calendar tab so the link shows current data.
3. Click **Notify Team**.

Your email application opens a draft:

- **To:** members of the user group who have email addresses
- **CC:** Scott.Christian@bmsgroup.com
- **Subject:** `{Client} {Project}: Project Update`
- **Body:** *Please review the Analytics Calendar for this project:* plus a link to the Calendar tab

Review the recipients, add any extra context, and send.

If you see *Select a User Group on the Project Info tab before notifying the team* or *No email addresses were found for members of the selected user group*, fix the group or member emails with a lead before trying again.

### 5.5 Open a project from an email link

Calendar notification emails include a link such as `/projects/{id}?tab=calendar`.

1. Click the link.
2. The project opens on the **Calendar** tab.
3. Review your items, update dates or notes, and **Save**.

Bookmark the project if you will return often. You can also open the same project from Home.

---

## 6. Entering time

There are two places to enter hours. They write the same project time:

| Place | Best for |
| --- | --- |
| **TM** weekly page | Your full week: all client projects plus non-client time |
| **Time tracker** tab on a project | Hours for one project while you are already in that project |

There is **no submit or approve step**. **Save time** writes the week immediately. Correct mistakes by editing the same week and saving again.

Enter time as **yourself**. For analysts, **CAT user** is locked to your signed-in account. Only Administrators can choose another CAT user.

### 6.1 Weekly Time Management

1. From the hub, click **Open TM**.
2. The page title is **TM Time Management**.
3. Confirm **CAT user** shows your name (read-only unless you are an Administrator).
4. Set the week with **Previous week**, **This week**, or **Next week**. Weeks run **Monday–Sunday**. The range and your name appear next to the buttons.
5. Enter hours on **Client Time** and/or **Non-Client Time**.
6. Click **Save time**. Both tabs save together for that user and week.

Shortcuts on the page: **Workload dashboard** (TM Dashboard) and **Back to home** (hub).

If you see *No projects found for this user via user-group membership*, you are not on any project user group. Ask to be added.

### 6.2 Client Time

Projects are grouped as **Online Projects** and **Offline Projects**, with the same online dots used in CM.

Each project block shows the project title, how many services/tasks it has, and the hours already entered for the week.

1. Expand a project (or use **Expand all** / **Collapse all**).
2. For each **Service / task** row, type hours under **Mon** through **Sun**. Leave a cell blank for zero.
3. Day totals and the week total update as you type.
4. **Open project** jumps to that project’s **Time tracker** tab if you need calendar or file context.

If a project shows *No calendar services / tasks on this project yet*, nothing can be charged until someone adds calendar items in CM and saves them.

Hours must be between **0 and 24** per cell.

### 6.3 Non-Client Time

Open the **Non-Client Time** tab for time that is not a client project.

Standard categories:

- Meetings
- Holidays
- PTO
- Travel
- Training
- CSI
- Moodys MAT
- Verisk MAT
- Peril Advisory
- Administrative

**To add a category row**

1. Choose **Category**.
2. Add it to the week.
3. Enter hours by day, the same way as client time.

Only add the categories you actually used that week. Empty categories do not need to sit on the grid.

### 6.4 Save time

Click **Save time**. A success message such as *Time week saved.* confirms the write.

**Remember**

- Save applies to **both** Client Time and Non-Client Time for the selected week.
- Changing week or CAT user without saving discards unsaved hours on screen.
- There is no manager approval queue. Saved time is live for dashboards and project reports.

Aim to complete each week as you go. The TM Dashboard flags weeks with some time logged but **less than 32 hours** as incomplete.

### 6.5 Project Time tracker

On an existing project, open **Time tracker**.

1. Choose the CAT user (tabs or selector for CAT members on the project user group).
2. Navigate the week as in TM.
3. Enter hours against each calendar **Service / task**.
4. Click **Save time**.

If there are no calendar items, the tab tells you to add them on **Calendar** (or **Add to Project** on Services) and save before tracking time.

**Allocate block of time** (project Time tracker only)

Use this when you know the day’s total but not the split across tasks:

1. Under **Allocate block of time**, choose the **Day** and **Hours** (for example 6.5).
2. Select the tasks to include (**Select all** or a subset).
3. Distribute. Hours are split evenly and marked **Allocated** (distinct from **Exact** typed cells).

Allocated hours still count toward the week. Use Exact when you know the real split.

### 6.6 TM Dashboard

In TM, click **Dashboard** in the sidebar, or **Workload dashboard** from time entry. The title is **TM Dashboard**.

Filters:

- **Year**
- **CAT user** — one person, or **All CAT users**

KPIs include total hours, project vs other, utilization, capacity, client/project counts, and incomplete weeks.

Charts and tables cover hours by analyst, project vs other mix, categories, clients, projects, monthly trends (including prior year), top tasks, exact vs allocated, and **Incomplete weeks (< 32h logged)**.

Exports:

- **Export utilization CSV**
- **Export hours detail CSV**

Use this page to check that your weeks are complete and to see where your time went. Leads use **All CAT users** to review the team.

**Time entry** on this page returns you to weekly TM. **Back to home** returns to the hub.

---

## 7. Reports and dashboards

### 7.1 Generate a project report

Create a PDF snapshot of the project from:

- Home: right-click a row → **Generate Report**
- Any project tab: **Generate Report** (the project must already be saved)

The report typically includes:

- Client and project information
- User group roster and roles
- Data files / sources
- Services
- Analytics calendar (type, title, owner, dates, notes)
- Time by analyst and time by task
- Notes

Wait until generation finishes, then open or save the PDF. Use this for status reviews, handoffs, and attachments to client or internal mail.

### 7.2 Generate Report & Send

From Home, right-click → **Generate Report & Send**.

The app builds the same PDF, then opens an Outlook draft (`.eml`) with the report attached and CAT CC addresses included. Review the To/CC lines and message, then send from Outlook.

This is the preferred way to email a full project pack. For a **single data file**, use **Email file** on **Project Data** instead.

### 7.3 PM Dashboard

In CM, click **Dashboard**. The page title is **Dashboard**. This is the portfolio view, not the TM utilization view.

Global filters include **Business Unit**, **Business Class**, **Project Status**, **Client Status**, and a date range.

Tabs:

| Tab | What you will see |
| --- | --- |
| **Executive Overview** | Renewal timeline, revenue tiers, aging, inactive projects; export Executive PDF |
| **Project Portfolio & Pipeline** | Begin-month / pipeline charts, project type views |
| **Service Coverage** | Service matrix by client or project |
| **Client Health** | Status and revenue health |

Use this to answer “what is in flight?”, “what is aging?”, and “which clients have coverage gaps?” Analytics Leads will live here more than Analyst II, but every analyst should know how to open it and drill to a project.

### 7.4 Drill from a chart into a project

1. Click a chart segment or bar.
2. A drill-down grid lists the underlying projects.
3. Open a row to **Edit Project**.

Clear the drill (or change filters) to return to the full dashboard. If you filter to a lead, business unit, or status on Home, confirm you are looking at the same slice on the dashboard before you draw conclusions.

---

## 8. CatAI (live)

> **Status (Build 1.2.0):** CatAI **is live**. It is connected to the LLM. You can send questions, keep conversation history, use tool lookups, and run create wizards. This is no longer a preview and is not waiting on a future release.

CatAI is a third application on the **Select Application** hub, next to Client Management and Time Management. It is **not** in the Client Management sidebar. You must be signed in to use it.

The hub tile title is **CatAI**. The description reads: *Ask questions about clients, projects, and work in natural language.* Click **Open CatAI**.

The greeting explains that chat **remembers prior messages**. Use **New chat** when you want a clean slate. You can ask about clients, projects, or user groups, or say you want to create a renewal project or user group.

**How to use it**

1. Click **Home** (if you are not already on the hub).
2. Click **Open CatAI**.
3. Choose a saved chat under **Chats**, or click **New chat**.
4. Type in **Message** and click **Send**.
5. Follow-up questions stay in the same thread. **Sources used** may list tools CatAI called (for example to look up a client or project).

**Wizards**

If you ask to create a **renewal project** or a **user group**, CatAI may start a guided **Wizard**. Answer each prompt, or click **Cancel wizard** to return to normal chat.

**What to remember**

- Treat answers as a starting point. Confirm anything that affects a deliverable, calendar owner, or time entry in the project itself.
- If you see *Unable to reach CatAI right now*, wait and try again, or work from CM and TM.
- CatAI does not replace calendar, time, or report workflows.

Administrators can change CatAI connection settings on the Admin **CatAI** tab. Analysts do not need that tab.

---

## 9. What not to do

The application currently does not block Admin or delete actions. That does not mean analysts should use them. Stay out of the following unless a lead or administrator has given you a specific task.

**Admin console** (`Admin` in the sidebar)

| Tab | Why it is not analyst work |
| --- | --- |
| **Server** | Database connection settings |
| **CatAI** (Admin) | LLM connection settings — administrators only |
| **Business Unit** / **Business Class** / **Client Type** / **Project Types** | Reference data for the whole firm |
| **Users** | User records, photos, bulk import, hard delete |
| **User Role** | Role catalog (Analyst I, Analytics Lead, and so on) |
| **UserGroup** | Creating groups and assigning members — ask a lead to add you to a team |
| **Clients** (Admin) | Firm-wide client maintenance and bulk import |
| **Services** / **Service Category** | Service catalog |
| **Data Audit** | Linkage checks (missing groups, missing Analytics Lead) — operations/lead use |

**Home and project actions to avoid**

- **Bulk import** of clients/projects from CSV
- **Delete** / **Hard delete** on the Home grid (permanent)
- **Soft-delete** of clients or projects, except when instructed
- Entering **another CAT user’s** time (analysts cannot; only Administrators can pick another user)
- Changing **user group** membership or dropping a group from a live project
- Deleting calendar tasks that already have time
- Deleting revenue history rows without a lead’s confirmation

If you opened Admin by accident, click **Home** and return to CM or TM.

---

## 10. Quick reference

### 10.1 Glossary

| Term | Meaning |
| --- | --- |
| **CMS / Cat Management System** | This application |
| **CM** | Client Management — clients, projects, calendar, files, reports |
| **TM** | Time Management — weekly hours and utilization |
| **Build 1.2.0** | Application and manual build. Shown on the hub under **Get Help** and on CM Home next to **Send Feedback** |
| **Hub / Select Application** | First screen after sign-in; choose CM, TM, or CatAI. Sidebar **Home** always returns here |
| **Get Help** | Hub link that opens this training manual (PDF) |
| **Sign in to launch** | CAT email + password gate. First launch creates your password |
| **User group** | The project team. Controls owners, Notify Team, and which projects appear in TM |
| **User role** | Job on that team (Analyst I, Analyst II, Analytics Lead, and others) |
| **Analytics Lead** | Analytics owner for the client/project; used in Home filters and dashboards |
| **CAT user** | CAT department person who can enter TM time |
| **Online project** | Marked in progress; grouped separately in TM and counted on Home |
| **Offline project** | Not currently in progress; still visible when you filter to Offline or All |
| **Calendar item** | Deliverable, meeting, task, or other row; also a time-entry task |
| **Owner** | User-group member responsible for a calendar item |
| **Notify Team** | Manual email to the user group with a calendar link |
| **Automatic owner email** | System email when every calendar item has an owner |
| **Exact time** | Hours you typed on a task/day |
| **Allocated time** | Hours split across tasks from **Allocate block of time** |
| **Incomplete week** | Some hours logged but fewer than 32 for the week |
| **Project report** | PDF pack of client, team, data, services, calendar, and time |
| **Project Data** | Uploaded analysis files (source, exposure, loss, claims, correspondence, submission) |
| **Analysis Profile** | Moody’s / Verisk BAR configuration on the project |
| **CatAI** | Hub chat assistant for clients, projects, and guided create wizards. Open from **Open CatAI**, not from the CM sidebar |
| **Also in CMS** | Presence list of other people currently signed in |

### 10.2 Common tasks checklist

**Start of day**

- [ ] **Sign in to launch** with your CAT email
- [ ] Confirm the hub shows **Build 1.2.0**
- [ ] Open the hub → **Open CM** (or sidebar **Project List**)
- [ ] Confirm Home is filtered the way you want (default is **Online**)
- [ ] Filter **Analytics Lead** or **Client** if you are focusing on a book of work
- [ ] Open each project you will work on today

**When you take on a project**

- [ ] Confirm you are on the **user group** (if the project is missing in TM, you are not)
- [ ] Review **Calendar**; add missing tasks or complete dates
- [ ] Assign **owners** and **Save**
- [ ] Click **Notify Team** if the group needs to review the calendar
- [ ] Upload or download **Project Data** as needed
- [ ] Add a **Note** for anything the next person must know

**When you enter time**

- [ ] Hub → **Open TM** (your **CAT user** is already your signed-in account)
- [ ] Set the correct week
- [ ] Enter **Client Time** against calendar tasks
- [ ] Enter **Non-Client Time** (PTO, meetings, and so on)
- [ ] **Save time**
- [ ] Optionally open **TM Dashboard** and confirm the week is not in **Incomplete weeks (< 32h)**

**When you need a report**

- [ ] Right-click the Home row → **Generate Report**, or use **Generate Report** on the project
- [ ] Use **Generate Report & Send** when the PDF should go out by email
- [ ] Use **Email file** on Project Data for a single file

**When you are lost**

- [ ] Sidebar **Home** → hub → choose **Open CM**, **Open TM**, or **Open CatAI**
- [ ] Home grid is under **Open CM** or **Project List**, not under sidebar Home
- [ ] CM **Dashboard** = portfolio; TM **Dashboard** = hours and utilization
- [ ] CatAI is a hub tile only — confirm answers in the project
- [ ] Use **Get Help** on the hub or **Send Feedback** on CM Home if something looks wrong
- [ ] **Sign out** from the hub when you leave a shared machine

---

## Appendix A. Technologies used

This appendix is a summary of the platforms, languages, and tools that make up the Cat Management System. Analysts do not need to install or configure these to use CM, TM, or CatAI. They are listed here so you know what sits behind the screens in this manual.

**How the application is built**

| Area | Technologies |
| --- | --- |
| Languages | C#, SQL (T-SQL), TypeScript / JavaScript, HTML, CSS, JSON |
| Server | .NET 8, ASP.NET Core (MVC and JSON APIs), Kestrel web server; IIS in typical hosted environments |
| User interface | React, React Router, Vite (the pages you use in the browser) |
| Database | Microsoft SQL Server (database **BMS_CMS**), stored procedures, ADO.NET (`Microsoft.Data.SqlClient`). Default sign-in to SQL is Windows Integrated Security |
| Files | Azure Blob Storage for project data files; local files for user photos and analysis profiles |
| Email | SMTP to Outlook / Exchange for automatic calendar-owner mail; browser **mailto:** and Outlook **.eml** drafts for Notify Team, reports, and file send |
| Reports | jsPDF (PDF project reports and dashboard export); CSV import and TM CSV export |
| CatAI | Azure OpenAI / Chat Completions, with saved conversations and tool calls (see Section 8) |
| Analysis profile | JSON templates aligned with Moody’s (AIR) and Verisk (RMS) BAR settings |
| Security | Cookie sign-in for CAT users; ASP.NET Core Data Protection (encrypted SQL and CatAI settings); anti-forgery tokens on form posts |

**What you may also see named in the environment**

- **Git** and **Visual Studio 2022** / **.NET 8 SDK** for building and deploying the application
- **Node.js** and **npm** for building the React front end
- **HTTPS** in local development (typically `https://localhost:7044`)

SQL Server, ASP.NET Core, and the React browser application are the core. Azure Blob Storage, SMTP/Outlook, and Azure OpenAI sit around that core as supporting services.

---

## Appendix B. What’s new in this build (1.2.0 vs 1.1)

This appendix summarizes user-facing changes between **Build 1.1** (the previous training manual, September 2026) and **Build 1.2.0**. Confirm **Build 1.2.0** on the hub Home screen before you use this list.

| Area | Previous build (1.1) | This build (1.2.0) |
| --- | --- | --- |
| **Build number** | Document version only (1.1). No build label in the UI | **Build 1.2.0** on the hub (under **Get Help**) and on CM Home (next to **Send Feedback**). This manual uses the same number |
| **Sign-in** | No login. Anyone who could open the app could use it | **Sign in to launch** for active CAT users. First launch creates a password; later visits use **Launch**. **Sign out** is on the hub |
| **Help** | No in-app manual link | **Get Help** on the hub opens this training manual (PDF) |
| **Who is signed in** | Not shown | Avatar stack and **Also in CMS** on every page after sign-in |
| **Hub** | CM, TM, and CatAI tiles | Same three tiles, plus **Get Help** and **Build 1.2.0**. The extra “Choose an application to continue” line is no longer on the earth hero |
| **CM navigation** | Sidebar **Home** returned to the hub only | **Project List** opens the Client / Project grid without going back through the hub |
| **Time entry** | You picked any **CAT user** from a dropdown | Analysts are **locked to their signed-in account**. Administrators can still pick another CAT user. **Save time** is still immediate (no submit/approve) |
| **CatAI** | Preview chat, **not** connected to an LLM | Live assistant: saved **Chats**, **New chat**, tool lookups (**Sources used**), and wizards to create a renewal project or user group |
| **Admin** | No CatAI settings tab | **CatAI** tab (administrators) for connection settings. Data Audit includes **Logins** |

**What did not change**

- Project tabs (Client Info, Project Info, Project Data, Services, Analysis Profile, Calendar, Time tracker, Revenue history, Notes)
- Calendar **Notify Team** and automatic owner emails
- Project report / Generate Report & Send
- TM Client Time vs Non-Client Time and the TM Dashboard

---

*End of Analyst Training Manual, Build 1.2.0*
