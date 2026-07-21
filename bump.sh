#!/bin/bash

CSPROJ="win-space.csproj"

if [ ! -f "$CSPROJ" ]; then
    echo -e "\033[31m错误: 找不到文件 $CSPROJ\033[0m"
    exit 1
fi

# 获取当前版本
CURRENT_VERSION=$(grep -o '<Version>[^<]*</Version>' "$CSPROJ" | sed -e 's/<Version>//' -e 's/<\/Version>//')
if [ -z "$CURRENT_VERSION" ]; then
    CURRENT_VERSION="1.0.0"
fi

# 如果通过参数传入了版本号，则跳过选择
VERSION="$1"

if [ -z "$VERSION" ]; then
    echo -e "当前版本: \033[36m$CURRENT_VERSION\033[0m"

    # 解析当前版本
    IFS='.' read -r major minor patch <<< "$CURRENT_VERSION"
    
    # 清理非数字字符 (防错)
    major=${major//[^0-9]/}
    minor=${minor//[^0-9]/}
    patch=${patch//[^0-9]/}
    
    major=${major:-0}
    minor=${minor:-0}
    patch=${patch:-0}

    # 计算下一个版本
    next_patch="${major}.${minor}.$((patch + 1))"
    next_minor="${major}.$((minor + 1)).0"
    next_major="$((major + 1)).0.0"

    echo ""
    echo -e "请选择升级的版本类型:"
    echo -e "  \033[36m1)\033[0m Patch (${next_patch})"
    echo -e "  \033[36m2)\033[0m Minor (${next_minor})"
    echo -e "  \033[36m3)\033[0m Major (${next_major})"
    echo -e "  \033[36m4)\033[0m Custom (自定义输入)"
    echo -e "  \033[36m0)\033[0m 取消"

    read -p "输入选项 [1-4]: " OPTION

    case $OPTION in
        1) VERSION="$next_patch" ;;
        2) VERSION="$next_minor" ;;
        3) VERSION="$next_major" ;;
        4) 
            read -p "请输入自定义版本号: " VERSION 
            ;;
        *)
            echo -e "\033[33m已取消。\033[0m"
            exit 0
            ;;
    esac
fi

if [ -z "$VERSION" ]; then
    echo -e "\033[33m版本号为空，已取消。\033[0m"
    exit 0
fi

# 移除可能存在的 'v' 前缀
VERSION="${VERSION#v}"
VERSION="${VERSION#V}"

# 替换或添加 Version 节点
if grep -q "<Version>.*</Version>" "$CSPROJ"; then
    sed -i "s|<Version>.*</Version>|<Version>$VERSION</Version>|" "$CSPROJ"
else
    # 如果没有找到，插入在第一个 </PropertyGroup> 之前
    awk '/<\/PropertyGroup>/ && !done { print "    <Version>'$VERSION'</Version>"; done=1 } 1' "$CSPROJ" > "${CSPROJ}.tmp" && mv "${CSPROJ}.tmp" "$CSPROJ"
fi

echo -e "\n\033[32m✔ 已将 $CSPROJ 的版本更新为 $VERSION\033[0m"

# 自动 Git 操作
COMMIT_MSG="chore: release v$VERSION"
TAG_NAME="v$VERSION"

echo -e "\n\033[33m正在自动执行 Git 操作...\033[0m"
git add "$CSPROJ"
git commit -m "$COMMIT_MSG"
git tag "$TAG_NAME"

echo -e "\n\033[33m正在推送到远程仓库...\033[0m"
git push
git push --tags

echo -e "\n\033[32m🎉 发布完成！\033[0m"
