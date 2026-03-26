#Used to parse items.xml to add tags for parents, add tags for child and include parent tags in child tags.
from __future__ import annotations

import argparse
from pathlib import Path
import xml.etree.ElementTree as ET
from xml.dom import minidom


def get_property_element(item: ET.Element, prop_name: str) -> ET.Element | None:
    for prop in item.findall("property"):
        name = prop.get("name")
        if name and name.lower() == prop_name.lower():
            return prop
    return None


def get_property_value(item: ET.Element, prop_name: str) -> str | None:
    prop = get_property_element(item, prop_name)
    return prop.get("value") if prop is not None else None


def split_tags(tag_value: str | None) -> list[str]:
    if not tag_value:
        return []
    return [part.strip() for part in tag_value.split(",") if part and part.strip()]


def unique_preserve_order(tags: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for tag in tags:
        if tag not in seen:
            seen.add(tag)
            result.append(tag)
    return result


def resolve_effective_tags(
    item_name: str,
    items_by_name: dict[str, ET.Element],
    cache: dict[str, list[str]],
    visiting: set[str] | None = None,
) -> list[str]:
    if item_name in cache:
        return cache[item_name]

    if visiting is None:
        visiting = set()
    if item_name in visiting:
        raise ValueError(f"Circular Extends chain detected at item '{item_name}'")

    item = items_by_name.get(item_name)
    if item is None:
        cache[item_name] = []
        return []

    own_tags = split_tags(get_property_value(item, "Tags"))
    if own_tags:
        cache[item_name] = unique_preserve_order(own_tags)
        return cache[item_name]

    extends_name = get_property_value(item, "Extends")
    if not extends_name:
        cache[item_name] = []
        return []

    visiting.add(item_name)
    inherited = resolve_effective_tags(extends_name, items_by_name, cache, visiting)
    visiting.remove(item_name)

    cache[item_name] = inherited[:]
    return cache[item_name]


def prettify_xml(root: ET.Element) -> str:
    rough = ET.tostring(root, encoding="utf-8")
    parsed = minidom.parseString(rough)
    pretty = parsed.toprettyxml(indent="  ", encoding="UTF-8").decode("utf-8")
    lines = [line for line in pretty.splitlines() if line.strip()]
    return "\n".join(lines) + "\n"


def build_patch(items_xml: Path, output_xml: Path) -> dict[str, int]:
    tree = ET.parse(items_xml)
    source_root = tree.getroot()

    items_by_name: dict[str, ET.Element] = {}
    for item in source_root.findall("item"):
        name = item.get("name")
        if name:
            items_by_name[name] = item

    resolved_cache: dict[str, list[str]] = {}
    configs = ET.Element("configs")

    stats = {
        "parent_append_existing_tags": 0,
        "parent_add_new_tags_property": 0,
        "child_materialized_from_parent_tags": 0,
        "child_add_name_only_no_parent_tags": 0,
        "child_append_existing_tags": 0,
        "skipped_already_has_own_name_tag": 0,
    }

    for item_name in sorted(items_by_name):
        item = items_by_name[item_name]
        extends_name = get_property_value(item, "Extends")
        own_tags_value = get_property_value(item, "Tags")
        own_tags = unique_preserve_order(split_tags(own_tags_value))

        if extends_name:
            if own_tags:
                if item_name in own_tags:
                    stats["skipped_already_has_own_name_tag"] += 1
                    continue

                append_el = ET.SubElement(
                    configs,
                    "append",
                    {"xpath": f"/items/item[@name='{item_name}']/property[@name='Tags']/@value"},
                )
                append_el.text = "," + item_name
                stats["child_append_existing_tags"] += 1
                continue

            parent_tags = resolve_effective_tags(extends_name, items_by_name, resolved_cache)
            cleaned_parent_tags = [tag for tag in parent_tags if tag != extends_name]
            final_tags = unique_preserve_order([item_name, *cleaned_parent_tags])

            append_el = ET.SubElement(
                configs,
                "append",
                {
                    "xpath": f"/items/item[@name='{item_name}' and not(property[@name='Tags']) and not(property[@name='tags'])]"
                },
            )
            ET.SubElement(
                append_el,
                "property",
                {
                    "name": "Tags",
                    "value": ",".join(final_tags),
                },
            )

            if cleaned_parent_tags:
                stats["child_materialized_from_parent_tags"] += 1
            else:
                stats["child_add_name_only_no_parent_tags"] += 1
            continue

        # Parent / non-extending item
        if own_tags:
            if item_name in own_tags:
                stats["skipped_already_has_own_name_tag"] += 1
                continue

            append_el = ET.SubElement(
                configs,
                "append",
                {"xpath": f"/items/item[@name='{item_name}']/property[@name='Tags']/@value"},
            )
            append_el.text = "," + item_name
            stats["parent_append_existing_tags"] += 1
        else:
            append_el = ET.SubElement(
                configs,
                "append",
                {
                    "xpath": f"/items/item[@name='{item_name}' and not(property[@name='Tags']) and not(property[@name='tags'])]"
                },
            )
            ET.SubElement(
                append_el,
                "property",
                {
                    "name": "Tags",
                    "value": item_name,
                },
            )
            stats["parent_add_new_tags_property"] += 1

    output_xml.write_text(prettify_xml(configs), encoding="utf-8")
    return stats


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate a 7DTD itemsmod.xml patch that adds item-name tags."
    )
    parser.add_argument("input", nargs="?", default="items.xml")
    parser.add_argument("output", nargs="?", default="itemsmod.xml")
    args = parser.parse_args()

    stats = build_patch(Path(args.input), Path(args.output))
    print(f"Generated: {args.output}")
    for key, value in stats.items():
        print(f"{key}: {value}")


if __name__ == "__main__":
    main()
