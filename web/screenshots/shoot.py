"""
Screenshots of the built React app against a mocked API, at phone and desktop widths.

Run by .github/workflows/screenshots.yml, which pushes the PNGs to the `screenshots` branch.
It exists so the UI can be checked visually from environments that can't run the app
(an AI assistant's sandbox, a phone). Data is invented; nothing touches a real database.

    python3 web/screenshots/shoot.py http://localhost:4173 out/
"""
import datetime
import json
import sys
from playwright.sync_api import sync_playwright

BASE, OUT = sys.argv[1].rstrip("/"), sys.argv[2].rstrip("/")
now = datetime.datetime.now(datetime.timezone.utc)


def iso(minutes_ago=0):
    return (now - datetime.timedelta(minutes=minutes_ago)).isoformat().replace("+00:00", "Z")


ALEX, SAM, HOUSEHOLD = "0199e000-0000-7000-8000-000000000001", "0199e000-0000-7000-8000-000000000002", "0199e000-0000-7000-8000-000000000003"
categories = [{"id": f"c{i}", "name": n, "sortOrder": i} for i, n in enumerate(["Short term", "Long term", "House"])]


def task(i, title, status, priority="medium", points=0, category="c0", assignee=None, due=None, description=None):
    done = status == "done"
    return {"id": f"0199e000-0000-7000-8000-0000000001{i:02}", "title": title, "description": description, "status": status,
            "priority": priority, "points": points, "categoryId": category, "assigneeId": assignee, "dueOn": due,
            "createdBy": ALEX, "createdAt": iso(1000 + i), "updatedAt": iso(i), "completedAt": iso(5) if done else None,
            "completedBy": SAM if done else None, "version": 1}


tomorrow = (now + datetime.timedelta(days=1)).date().isoformat()
tasks = [
    task(1, "Take the bins out", "todo", "high", 5, "c2", SAM, tomorrow),
    task(2, "Book the car MOT", "todo", "urgent", 10, "c0", None, tomorrow, "Garage on Mill Lane, ask about the brakes too"),
    task(3, "Clear out the loft", "doing", "low", 50, "c1", ALEX),
    task(4, "Hoover the stairs", "done", "medium", 5, "c2", SAM),
    task(5, "Fix the dripping tap in the upstairs bathroom before it gets worse", "todo", "medium", 15, "c2", ALEX),
]
items = [
    {"id": f"0199e000-0000-7000-8000-0000000002{i:02}", "name": n, "quantity": q, "note": note, "addedBy": ALEX,
     "addedAt": iso(300 - i), "checkedBy": None, "checkedAt": None}
    for i, (n, q, note) in enumerate([("Milk", "2", "semi-skimmed"), ("Bread", None, None),
                                      ("Washing-up liquid", None, "the lemon one"), ("Bananas", "6", None),
                                      ("A really long item name that should wrap nicely on a small phone screen", "1 big bag", None)])
]
checked = [{"id": "0199e000-0000-7000-8000-000000000301", "name": "Coffee", "quantity": None, "note": None, "addedBy": ALEX,
            "addedAt": iso(900), "checkedBy": SAM, "checkedAt": iso(30)}]
members = [{"userId": ALEX, "displayName": "Alex Example", "avatarUrl": None, "role": "admin", "joinedAt": iso(90000)},
           {"userId": SAM, "displayName": "Sam Example", "avatarUrl": None, "role": "member", "joinedAt": iso(80000)}]
activity = [
    {"id": "a1", "type": "TaskCompleted", "actorId": SAM, "payload": {"title": "Hoover the stairs", "points": 5}, "occurredAt": iso(5)},
    {"id": "a2", "type": "ShoppingItemChecked", "actorId": SAM, "payload": {"name": "Coffee"}, "occurredAt": iso(30)},
    {"id": "a3", "type": "ShoppingItemAdded", "actorId": ALEX, "payload": {"name": "Bananas", "quantity": "6"}, "occurredAt": iso(296)},
]
me = {"id": ALEX, "displayName": "Alex Example", "email": "alex@example.com", "avatarUrl": None}
health = {"status": "Healthy", "checks": {"postgres": "Healthy"}}

SCENARIOS = {
    # name: (hash, responses by path; a number means "fail with that status")
    "board": ("#board", {}),
    "shopping": ("#shopping", {}),
    "signed-out": ("", {"/api/identity/me": 401}),
    "no-household": ("", {"/api/households": []}),
    "board-errors": ("#board", {"/api/tasks": 500, "/api/points/leaderboard": 500, "/api/activity": 500}),
    "shopping-errors": ("#shopping", {"/api/shopping": 500, "/api/shopping/checked": 500}),
}

DEFAULTS = {
    "/api/identity/me": me, "/api/households": [{"id": HOUSEHOLD, "name": "The Examples", "role": "admin"}],
    "/api/tasks": tasks, "/api/tasks/categories": categories, "/api/households/members": members,
    "/api/shopping": items, "/api/shopping/checked": checked, "/api/activity": activity,
    "/api/points/leaderboard": [{"userId": SAM, "points": 45}, {"userId": ALEX, "points": 20}], "/api/health": health,
}


def handler(overrides):
    def handle(route):
        path = "/" + route.request.url.split("/", 3)[3].split("?")[0]
        if route.request.method != "GET":
            return route.fulfill(status=500, content_type="application/problem+json",
                                 body=json.dumps({"title": "Error", "detail": "Simulated failure"}))
        body = overrides.get(path, DEFAULTS.get(path))
        if body is None:
            return route.fulfill(status=404, body="")
        if isinstance(body, int) and not isinstance(body, bool):
            return route.fulfill(status=body, content_type="application/problem+json",
                                 body=json.dumps({"title": "Error", "status": body}))
        route.fulfill(status=200, content_type="application/json", body=json.dumps(body))
    return handle


with sync_playwright() as p:
    browser = p.chromium.launch()
    for width, device in [(390, "phone"), (1280, "desktop")]:
        for name, (hash_, overrides) in SCENARIOS.items():
            page = browser.new_page(viewport={"width": width, "height": 844}, device_scale_factor=1)
            page.route("**/api/**", handler(overrides))
            page.goto(f"{BASE}/{hash_}")
            page.wait_for_timeout(1500)
            page.screenshot(path=f"{OUT}/{device}-{name}.png", full_page=True)
            page.close()
    browser.close()
print(f"Saved screenshots to {OUT}")
