The full transcript is in the file below. It's a machine transcript of a 19-minute recording, so the isiZulu parts and overlapping voices are rough. These are the changes the lecturer asked for, grouped by use case:

**1. Stock management: split into two use cases**
- **Receive Stock**: stock comes in from orders placed with suppliers, and receiving it increases stock levels.
- **Record Ingredient Usage**: the chef records something like "20 burgers," and the recipe automatically deducts each ingredient (bun, patty, etc.). The system doesn't currently do this, so it has to change.
- Remove the screen where a user can just edit a quantity by hand (for example, changing 20 burgers to 10).

**2. Meal preparation & kitchen schedule**
- Split this into two separate use cases, and clearly define who the actor is. Nobody could answer that question during the consultation.
- Don't have staff capture the number of meals or portions (the "250" field). Meal counts should come from the orders automatically.
- Use the prep time per meal to automatically work out how many kitchen staff are needed.

**3. Meal attendance tracking → rename to "Collect Meal"**
- Use one actor only. Room or location codes can't be actors.
- A triggering event must be something that has already happened. "Schedule a meal session" doesn't count, and there are too many triggering events.
- Cut the 17 steps down and keep the use case as simple as possible.
- When the learner's card or QR code is scanned, it must show the meal they pre-ordered. This works like an order number on a KFC receipt, so staff hand over the correct meal.

**4. Meal compliance report (Housemaster)**
- One actor, the Housemaster. Remove admin, kitchen, and system from the actor list.
- Add period filters (date range, weekly, monthly).
- Decide whether it shows a summary or per-student detail. A list of 1,000 students isn't usable, so it should be a summary first.
- On the "pre-order vs actual" report, rename "attendance" to something like "ordered vs collected."
- Don't list every single date; aggregate over the selected period.

**5. Meal quantity & feedback management**
- The lecturer said there's "nothing much" there, so it needs to be fleshed out.

**6. General**
- Read the use case guidelines document. The lecturer was clearly frustrated that it wasn't followed.
- Load test data so the reports actually show something.
- The work needs to look polished because the moderator will be at the final presentation.

**Deadlines mentioned**
- **Thursday:** update the document and submit the final version.
- **Friday:** it gets sent to the moderator.
- The final presentation, with the moderator present, was mentioned as "next week." Confirm the exact date with your group, because the recording is a bit unclear there.
