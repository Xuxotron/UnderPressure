import json
import re
import struct
import UnityPy

env = UnityPy.load(r"D:\Games\Two Point Hospital\TPH_Data\sharedassets3.assets")
by_id = {obj.path_id: obj for obj in env.objects}
name = "Config_CharacterTraits"
obj = next(o for o in env.objects if o.type.name == "MonoBehaviour" and o.peek_name() == name)
raw = obj.get_raw_data()
offset = (32 + len(name) + 3) // 4 * 4
count = struct.unpack_from("<I", raw, offset)[0]
pointers = [struct.unpack_from("<iq", raw, offset + 4 + i * 12)[1]
            for i in range(count)]
config = json.loads(re.search(rb"\{[^\x00]+\}", raw).group())
print("min/max:", config["GameplayTraitsMin"], config["GameplayTraitsMax"])
for entry in config["CharacterTraits"]["List"]:
    index = entry["Definition"]
    ref = by_id[pointers[index]]
    definition = json.loads(re.search(rb"\{[^\x00]+\}", ref.get_raw_data()).group())
    print(index, ref.peek_name(), "weight", entry["Weight"], "modifiers",
          definition.get("Modifiers", []))
