import UnityPy, sys, json
env = UnityPy.load(r'D:\Games\Two Point Hospital\TPH_Data\sharedassets3.assets')
objs = {o.path_id:o for o in env.objects if o.assets_file.name == 'sharedassets3.assets'}
def get(ptr): return objs[ptr['m_PathID']].read_typetree()
def dump(go_id, depth=0, limit=4):
    go=objs[go_id].read_typetree()
    print('  '*depth+'GO '+str(go_id)+' '+go['m_Name'])
    tr=None
    for c in go['m_Component']:
        ob=objs[c['component']['m_PathID']]
        try: data=ob.read_typetree()
        except ValueError: data=ob.read_typetree(check_read=False)
        if ob.type.name in ('RectTransform','Transform'):
            tr=data
            print('  '*depth+json.dumps({k:v for k,v in data.items() if k not in ('m_GameObject','m_Children','m_LocalRotation')},ensure_ascii=False))
        elif ob.type.name=='MonoBehaviour':
            print('  '*depth+'COMP '+str(ob.path_id)+' '+json.dumps(data,ensure_ascii=False))
    if depth<limit and tr:
        for c in tr['m_Children']:
            t=get(c); dump(t['m_GameObject']['m_PathID'],depth+1,limit)
dump(int(sys.argv[1]),limit=int(sys.argv[2]) if len(sys.argv)>2 else 3)
