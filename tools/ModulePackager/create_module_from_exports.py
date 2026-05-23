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
import sys
import tempfile
import zipfile
from datetime import date
from pathlib import Path


SUPPORTED_TEMPLATE_EXTENSIONS = {".xls", ".xlsx", ".et", ".ett"}


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


def create_database(db_path: Path, manifest: dict[str, object], templates: list[tuple[Path, str]]) -> None:
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

        connection.execute(
            """
            INSERT INTO TemplateCategory (Id, ParentId, Name, Level, SortOrder, CategoryType)
            VALUES (1, NULL, ?, 1, 10, ?);
            """,
            (f"{manifest['province']}{manifest['major']}{manifest['year']}", "资料表"),
        )

        for index, (source_path, stored_path) in enumerate(templates, start=1):
            name = source_path.stem
            connection.execute(
                """
                INSERT INTO TemplateItem (
                  Id, CategoryId, TemplateName, TemplateCode, TemplateFile,
                  TemplateType, SortOrder, IsEnabled
                )
                VALUES (?, 1, ?, ?, ?, ?, ?, 1);
                """,
                (index, name, f"T-{index:04d}", stored_path, "资料表", index * 10),
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

        copied: list[tuple[Path, str]] = []
        for index, source in enumerate(templates, start=1):
            target_name = f"{index:04d}_{sanitize_file_name(source.stem)}{source.suffix.lower()}"
            shutil.copy2(source, template_root / target_name)
            copied.append((source, target_name))

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
