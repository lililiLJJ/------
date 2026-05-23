"""
Create a custom .module package from manually exported Excel templates.

The generated package follows the app's own module format:
manifest.json + rules.db + templates/ + previews/ + config/, zipped as .module.
"""

from __future__ import annotations

import argparse
import json
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import dataclass
from datetime import date
from pathlib import Path


SUPPORTED_TEMPLATE_EXTENSIONS = {".xls", ".xlsx", ".et", ".ett"}


@dataclass(frozen=True)
class PackagedTemplate:
    source_path: Path
    stored_path: str
    category_parts: tuple[str, ...]


def sanitize_file_name(value: str) -> str:
    invalid = '<>:"/\\|?*'
    result = "".join("_" if char in invalid else char for char in value).strip()
    return result or "未命名"


def normalize_module_id(value: str) -> str:
    result = []
    for char in value.lower():
        if char.isalnum():
            result.append(char)
        elif char in {"-", "_", " "}:
            result.append("_")
    normalized = "".join(result).strip("_")
    return normalized or "custom_module"


def collect_templates(input_dir: Path) -> list[Path]:
    files = [
        path
        for path in input_dir.rglob("*")
        if path.is_file()
        and path.suffix.lower() in SUPPORTED_TEMPLATE_EXTENSIONS
        and not path.name.startswith("~$")
    ]
    return sorted(files, key=lambda path: str(path.relative_to(input_dir)).lower())


def create_manifest(args: argparse.Namespace, template_count: int) -> dict[str, object]:
    return {
        "moduleId": args.module_id,
        "name": args.name,
        "version": args.version,
        "province": args.province,
        "major": args.major,
        "year": args.year,
        "description": args.description or f"{args.province}{args.major}{args.year}模板库",
        "author": args.author,
        "templateCount": template_count,
        "database": "rules.db",
        "templateRoot": "templates",
        "createdAt": args.created_at or date.today().isoformat(),
    }


def resolve_category_type(level: int, is_leaf: bool) -> str:
    if is_leaf:
        return "资料表"

    return {
        1: "分部",
        2: "子分部",
        3: "分项",
        4: "检验批",
    }.get(level, "资料表")


def create_database(db_path: Path, manifest: dict[str, object], templates: list[PackagedTemplate]) -> None:
    connection = sqlite3.connect(db_path)
    try:
        connection.executescript(
            """
            CREATE TABLE ModuleInfo (
              Id INTEGER PRIMARY KEY,
              ModuleId TEXT NOT NULL,
              Name TEXT NOT NULL,
              Version TEXT NOT NULL,
              Province TEXT NOT NULL,
              Major TEXT NOT NULL,
              Year TEXT NOT NULL,
              Description TEXT,
              Author TEXT,
              CreatedAt TEXT NOT NULL
            );

            CREATE TABLE TemplateCategory (
              Id INTEGER PRIMARY KEY,
              ParentId INTEGER NULL,
              Name TEXT NOT NULL,
              Level INTEGER NOT NULL,
              SortOrder INTEGER NOT NULL,
              CategoryType TEXT NOT NULL
            );

            CREATE TABLE TemplateItem (
              Id INTEGER PRIMARY KEY,
              CategoryId INTEGER NOT NULL,
              TemplateName TEXT NOT NULL,
              TemplateCode TEXT,
              TemplateFile TEXT NOT NULL,
              TemplateType TEXT NOT NULL,
              SortOrder INTEGER NOT NULL,
              IsEnabled INTEGER NOT NULL
            );

            CREATE TABLE InspectionRule (
              Id INTEGER PRIMARY KEY,
              TemplateItemId INTEGER NOT NULL,
              RuleType TEXT NOT NULL,
              ItemName TEXT NOT NULL,
              Requirement TEXT,
              CheckMethod TEXT,
              AllowedDeviation TEXT,
              SortOrder INTEGER NOT NULL,
              Source TEXT
            );

            CREATE TABLE TemplateField (
              Id INTEGER PRIMARY KEY,
              TemplateItemId INTEGER NOT NULL,
              FieldName TEXT NOT NULL,
              FieldType TEXT,
              CellAddress TEXT,
              DefaultValue TEXT,
              SortOrder INTEGER NOT NULL
            );
            """
        )

        connection.execute(
            """
            INSERT INTO ModuleInfo (
              Id, ModuleId, Name, Version, Province, Major, Year,
              Description, Author, CreatedAt
            )
            VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            (
                manifest["moduleId"],
                manifest["name"],
                manifest["version"],
                manifest["province"],
                manifest["major"],
                manifest["year"],
                manifest["description"],
                manifest["author"],
                manifest["createdAt"],
            ),
        )

        category_ids: dict[tuple[str, ...], int] = {}
        next_category_id = 1
        sorted_category_paths = sorted(
            {
                template.category_parts[:depth]
                for template in templates
                for depth in range(1, len(template.category_parts) + 1)
            },
            key=lambda parts: (len(parts), parts),
        )
        if any(not template.category_parts for template in templates):
            sorted_category_paths.insert(0, ("资料表",))
            category_ids[()] = 1

        for parts in sorted_category_paths:
            if parts in category_ids.values():
                continue

            parent_parts = parts[:-1]
            parent_id = category_ids.get(parent_parts)
            category_id = next_category_id
            next_category_id += 1
            category_ids[parts] = category_id
            if parts == ("资料表",) and () in category_ids:
                category_ids[()] = category_id

            level = len(parts)
            has_child_category = any(
                other != parts and len(other) > len(parts) and other[: len(parts)] == parts
                for other in sorted_category_paths
            )
            connection.execute(
                """
                INSERT INTO TemplateCategory (Id, ParentId, Name, Level, SortOrder, CategoryType)
                VALUES (?, ?, ?, ?, ?, ?);
                """,
                (
                    category_id,
                    parent_id,
                    parts[-1],
                    level,
                    category_id * 10,
                    resolve_category_type(level, not has_child_category),
                ),
            )

        for index, template in enumerate(templates, start=1):
            name = template.source_path.stem
            category_id = category_ids[template.category_parts or ("资料表",)]
            connection.execute(
                """
                INSERT INTO TemplateItem (
                  Id, CategoryId, TemplateName, TemplateCode, TemplateFile,
                  TemplateType, SortOrder, IsEnabled
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, 1);
                """,
                (index, category_id, name, f"T-{index:04d}", template.stored_path, "资料表", index * 10),
            )

            connection.execute(
                """
                INSERT INTO InspectionRule (
                  Id, TemplateItemId, RuleType, ItemName, Requirement,
                  CheckMethod, AllowedDeviation, SortOrder, Source
                )
                VALUES (?, ?, '说明', '待补充规则', '此模板由手动导出的表格生成，规范规则待后续整理。', '', NULL, 10, '手动导入');
                """,
                (index, index),
            )

        connection.commit()
    finally:
        connection.close()


def convert_xls_to_xlsx(source: Path, target: Path, converter_script: Path) -> None:
    command = [
        "powershell",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(converter_script),
        "-InputPath",
        str(source),
        "-OutputPath",
        str(target),
    ]
    result = subprocess.run(command, text=True, capture_output=True, check=False)
    if result.returncode != 0:
        details = (result.stderr or result.stdout).strip()
        raise RuntimeError(f"转换 .xls 失败：{source}。{details}")


def build_package(args: argparse.Namespace) -> None:
    input_dir = args.input_dir.resolve()
    output_path = args.output.resolve()
    if not input_dir.exists() or not input_dir.is_dir():
        raise RuntimeError(f"导入目录不存在：{input_dir}")

    templates = collect_templates(input_dir)
    if not templates:
        raise RuntimeError(f"导入目录没有找到模板文件：{input_dir}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.suffix.lower() != ".module":
        output_path = output_path.with_suffix(".module")

    manifest = create_manifest(args, len(templates))
    with tempfile.TemporaryDirectory(prefix="module-packager-") as temp:
        temp_path = Path(temp)
        template_root = temp_path / "templates"
        (temp_path / "previews").mkdir(parents=True)
        (temp_path / "config").mkdir(parents=True)
        template_root.mkdir(parents=True)

        copied: list[PackagedTemplate] = []
        converter_script = args.converter_script.resolve()
        for index, source in enumerate(templates, start=1):
            relative_parent = source.relative_to(input_dir).parent
            category_parts = tuple(
                part for part in relative_parent.parts
                if part and part != "."
            )
            source_extension = source.suffix.lower()
            target_extension = ".xlsx" if args.convert_xls_to_xlsx and source_extension == ".xls" else source_extension
            target_name = f"{index:04d}_{sanitize_file_name(source.stem)}{target_extension}"
            stored_path = "/".join((*category_parts, target_name))
            target_path = template_root / stored_path
            target_path.parent.mkdir(parents=True, exist_ok=True)
            if args.convert_xls_to_xlsx and source_extension == ".xls":
                convert_xls_to_xlsx(source.resolve(), target_path.resolve(), converter_script)
            else:
                shutil.copy2(source, target_path)
            copied.append(PackagedTemplate(source, stored_path, category_parts))

        (temp_path / "manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        (temp_path / "config" / "README.txt").write_text(
            "此模块由手动导出的 Excel/WPS 模板生成，规则数据可后续补充。\n",
            encoding="utf-8",
        )
        create_database(temp_path / "rules.db", manifest, copied)

        if output_path.exists():
            output_path.unlink()

        with zipfile.ZipFile(output_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(temp_path.rglob("*")):
                if path.is_file():
                    archive.write(path, path.relative_to(temp_path).as_posix())

    print(f"已生成模块包：{output_path}")
    print(f"模板数量：{len(templates)}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="从手动导出的表格目录生成自定义 .module 模块包")
    parser.add_argument("--input-dir", required=True, type=Path, help="手动导出的 .xls/.xlsx/.et/.ett 文件目录")
    parser.add_argument("--output", required=True, type=Path, help="输出 .module 文件路径")
    parser.add_argument("--module-id", default="gd_building_2024")
    parser.add_argument("--name", default="广东省房屋建筑工程竣工验收技术资料统一用表")
    parser.add_argument("--version", default="1.0.0")
    parser.add_argument("--province", default="广东")
    parser.add_argument("--major", default="房建")
    parser.add_argument("--year", default="2024")
    parser.add_argument("--description", default="")
    parser.add_argument("--author", default="自定义")
    parser.add_argument("--created-at", default="")
    parser.add_argument(
        "--convert-xls-to-xlsx",
        action="store_true",
        help="使用本机 Excel COM 将 .xls 批量转换为 .xlsx 后再打包",
    )
    parser.add_argument(
        "--converter-script",
        type=Path,
        default=Path(__file__).with_name("Convert-XlsToXlsx.ps1"),
        help="Excel COM 转换脚本路径",
    )
    args = parser.parse_args(argv)
    args.module_id = normalize_module_id(args.module_id)
    return args


def main(argv: list[str]) -> int:
    try:
        build_package(parse_args(argv))
        return 0
    except Exception as ex:
        print(f"ERROR: {ex}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
