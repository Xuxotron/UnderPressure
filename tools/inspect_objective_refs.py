import json
import re
import struct
import UnityPy

path = r"D:\Games\Two Point Hospital\TPH_Data\sharedassets3.assets"
env = UnityPy.load(path)
by_id = {obj.path_id: obj for obj in env.objects}
for suffix in ("LevelObjective1", "LevelObjective2", "LevelObjective3"):
    name = "Objective_R1_TutorialHospital_" + suffix
    obj = next(obj for obj in env.objects if obj.type.name == "MonoBehaviour" and obj.peek_name() == name)
    raw = obj.get_raw_data()
    offset = (32 + len(name) + 3) // 4 * 4
    count = struct.unpack_from("<I", raw, offset)[0]
    pointers = [struct.unpack_from("<iq", raw, offset + 4 + i * 12)[1] for i in range(count)]
    data = json.loads(re.search(rb"\{[^\x00]+\}", raw).group(0))
    print(name, [(i, by_id[p].peek_name() if p in by_id else p) for i, p in enumerate(pointers)])
    print([r for r in data.get("CompletionRewards", []) if "_definition" in r])
