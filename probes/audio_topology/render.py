from .classify import ClassificationRecord

def render_tree(tree: dict) -> str:
    lines = ["音訊端點接口拓樸"]
    def render_children(rows, prefix="  "):
        for index, node in enumerate(rows):
            last = index == len(rows) - 1
            branch = "└─ " if last else "├─ "
            if node.get("kind") == "display":
                lines.append(f"{prefix}{branch}🖥 {node['name']} [Windows]")
            elif node.get("kind") == "device":
                pld = f"（{node['pld']}）" if node.get("pld") else ""
                lines.append(f"{prefix}{branch}{node['name']}{pld} [{node.get('source', '未知')}]")
                render_children(node.get("children", []), prefix + ("   " if last else "│  "))
            else:
                lines.append(f"{prefix}{branch}{node['name']}（{node['flow']}，{node['category']} 類，{node['short_id']}）[{node['source']}]")
                for why in node.get("unresolved", []): lines.append(f"{prefix}{'   ' if last else '│  '}證據不足：{why}")
    for root in tree.get("roots", []):
        lines.append(f"{root['name']} [{root['source']}]")
        render_children(root.get("children", []))
    return "\n".join(lines)
