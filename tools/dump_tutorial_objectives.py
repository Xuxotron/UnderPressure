import json
import re
import UnityPy

env = UnityPy.load(r"D:\Games\Two Point Hospital\TPH_Data\sharedassets3.assets")
prefix = "Objective_R1_TutorialHospital_"
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    name = obj.peek_name() or ""
    if not name.startswith(prefix):
        continue
    match = re.search(rb"\{[^\x00]+\}", obj.get_raw_data())
    if not match:
        continue
    try:
        data = json.loads(match.group())
    except (ValueError, UnicodeDecodeError):
        continue
    if "SubGoalDefinitions" not in data:
        continue
    print(name)
    for goal in data["SubGoalDefinitions"]:
        print("  ", goal.get("$type"), {key: value for key, value in goal.items()
               if key not in ("AdviceText", "DisplayOnHUD", "OnceCompleteStayComplete", "Deprecated", "HiScoreWeight")})
    for reward in data.get("CompletionRewards", []):
        print("    reward", reward)
