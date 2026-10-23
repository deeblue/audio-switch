from .classify import ClassificationRecord

def render_tree(tree: dict) -> str:
    lines = ["音訊端點接口拓樸"]
    for root in tree.get("roots", []):
        lines.append(f"{root['name']} [{root['source']}]")
        for node in root.get("children", []):
            lines.append(f"  └─ {node['name']}（{node['flow']}，{node['category']} 類，{node['short_id']}）[{node['source']}]" )
            for why in node.get("unresolved", []): lines.append(f"      證據不足：{why}")
    return "\n".join(lines)
