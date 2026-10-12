import json
import re
import struct
import sys
from collections import Counter

import UnityPy


ROOT = r"D:\Games\Two Point Hospital\TPH_Data"


def wrapper_data(obj):
    raw = obj.get_raw_data()
    name = obj.peek_name()
    offset = (32 + len(name) + 3) // 4 * 4
    count = struct.unpack_from("<I", raw, offset)[0]
    refs = [struct.unpack_from("<iq", raw, offset + 4 + i * 12)
            for i in range(count)]
    text = raw.decode("utf-8", "ignore")
    start = text.find('{"')
    data = json.JSONDecoder().raw_decode(text[start:])[0] if start >= 0 else {}
    return name, data, refs


shared = UnityPy.load(ROOT + r"\sharedassets3.assets")
objects = {o.path_id: o for o in shared.objects}
database = next(o for o in shared.objects
                if o.type.name == "MonoBehaviour" and
                o.peek_name() == "DB_Items_Landscape")
_, database_json, database_refs = wrapper_data(database)
definition_ids = {
    database_refs[index][1]
    for index in database_json["RoomItems"]
    if 0 <= index < len(database_refs) and database_refs[index][0] == 0
}

prefab_to_definitions = {}
definition_names = {}
for path_id in definition_ids:
    obj = objects.get(path_id)
    if obj is None:
        continue
    name, data, refs = wrapper_data(obj)
    definition_names[path_id] = name
    prefab_index = data.get("_prefab")
    if isinstance(prefab_index, int) and 0 <= prefab_index < len(refs):
        file_id, prefab_id = refs[prefab_index]
        if file_id == 0 and prefab_id:
            prefab_to_definitions.setdefault(prefab_id, set()).add(path_id)

targets = set(definition_ids) | set(prefab_to_definitions)
prefab_name_to_definitions = {}
for prefab_id, definitions in prefab_to_definitions.items():
    prefab = objects.get(prefab_id)
    if prefab is None:
        continue
    try:
        prefab_name = prefab.read().m_Name
    except Exception:
        continue
    prefab_name_to_definitions.setdefault(prefab_name, set()).update(definitions)
level_number = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1] != "all" else None


def scan_level(level_number):
 environment = UnityPy.load(ROOT + rf"\level{level_number}")
 hits = Counter()
 hit_objects = []
 for assets_file in environment.files.values():
    external_id = None
    for index, external in enumerate(assets_file.externals, 1):
        if external.path.replace("\\", "/").lower().endswith("sharedassets3.assets"):
            external_id = index
            break
    if external_id is None:
        continue
    for obj in assets_file.objects.values():
        try:
            raw = obj.get_raw_data()
        except Exception:
            continue
        local_hits = set()
        for offset in range(0, len(raw) - 11, 4):
            if struct.unpack_from("<i", raw, offset)[0] != external_id:
                continue
            path_id = struct.unpack_from("<q", raw, offset + 4)[0]
            if path_id in targets:
                local_hits.add(path_id)
        if local_hits:
            for path_id in local_hits:
                hits[path_id] += 1
            hit_objects.append((obj.path_id, obj.type.name, sorted(local_hits)))

 scene_names = Counter()
 for obj in environment.objects:
    if obj.type.name != "GameObject":
        continue
    try:
        scene_names[obj.read().m_Name] += 1
    except Exception:
        pass
 matched_names = set(scene_names).intersection(prefab_name_to_definitions)
 matched_definitions = set()
 for name in matched_names:
    matched_definitions.update(prefab_name_to_definitions[name])
 return environment, hits, hit_objects, scene_names, matched_names, matched_definitions


if level_number is None:
    build = UnityPy.load(ROOT + r"\globalgamemanagers")
    build_settings = next(o.read() for o in build.objects if o.type.name == "BuildSettings")
    provenance = {}
    scene_relations = set()
    for index, scene in enumerate(build_settings.scenes):
        if index < 7 or index == 23:
            continue
        _, _, _, _, matched_names, matched_definitions = scan_level(index)
        if "/Final/" in scene or index in (7, 8, 9):
            group = "Base"
        else:
            match = re.search(r"/DLC(\d*)/", scene)
            group = "DLC" + (match.group(1) or "1") if match else "Unknown"
        for definition in matched_definitions:
            provenance.setdefault(definition, set()).add(group)
        scene_name = scene.rsplit("/", 1)[-1].removesuffix(".unity")
        for prefab_name in matched_names:
            scene_relations.add((prefab_name, scene_name))
        print(index, group, len(matched_names), len(matched_definitions), scene)
    counts = Counter()
    for groups in provenance.values():
        counts["+".join(sorted(groups))] += 1
    print("provenance", len(provenance), "of", len(definition_ids), "groups", counts)
    exclusive = Counter()
    for definition, groups in provenance.items():
        if "Base" not in groups and len(groups) == 1:
            exclusive[next(iter(groups))] += 1
    print("exclusive", exclusive)
    if len(sys.argv) > 2:
        with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as output:
            output.write("# Generado desde las escenas originales de Two Point Hospital.\n")
            output.write("# Nombre exacto del prefab<TAB>SceneName que lo utiliza.\n")
            for prefab_name, scene_name in sorted(scene_relations):
                output.write(prefab_name + "\t" + scene_name + "\n")
    sys.exit(0)

environment, hits, hit_objects, scene_names, matched_names, matched_definitions = scan_level(level_number)

print("level", level_number, "definitions", len(definition_ids),
      "prefabs", len(prefab_to_definitions), "hits", len(hits),
      "objects", len(hit_objects))
for path_id, count in hits.most_common(100):
    if path_id in definition_names:
        print("definition", count, path_id, definition_names[path_id])
    else:
        names = [definition_names[x] for x in prefab_to_definitions[path_id]]
        print("prefab", count, path_id, " | ".join(sorted(names)[:5]))

print("name matches", len(matched_names), "definitions", len(matched_definitions))
for name in sorted(matched_names)[:100]:
    definitions = prefab_name_to_definitions[name]
    print("name", scene_names[name], name, "=>",
          " | ".join(sorted(definition_names[x] for x in definitions)[:5]))
