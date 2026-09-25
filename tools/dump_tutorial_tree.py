import re
import sys
import UnityPy

env = UnityPy.load(r"D:\Games\Two Point Hospital\TPH_Data\sharedassets3.assets")
obj = next(o for o in env.objects if o.type.name == "MonoBehaviour"
           and o.peek_name() == "Objective_R1_TutorialHospital_LevelScript_BT")
data = obj.get_raw_data()
minimum = int(sys.argv[1]) if len(sys.argv) > 1 else 0
maximum = int(sys.argv[2]) if len(sys.argv) > 2 else len(data)
for match in re.finditer(rb"[ -~]{6,}", data):
    if minimum <= match.start() < maximum:
        value = match.group().decode('ascii')
        print(f"{match.start():6} {value[:240]}")
