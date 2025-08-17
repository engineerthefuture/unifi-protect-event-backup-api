#!/bin/bash

# Cyclomatic Complexity Analysis Script
echo "🔍 CYCLOMATIC COMPLEXITY ANALYSIS 🔍"
echo "====================================="

# Navigate to project root
cd "$(dirname "$0")"

echo "📊 Method 1: Using SonarAnalyzer warnings..."
echo ""

# Build with detailed warnings to see complexity warnings
dotnet build --verbosity normal -p:TreatWarningsAsErrors=false -p:WarningsAsErrors="" 2>&1 | grep -E "(CS8618|S1541|S3776|complexity)" || echo "No complexity warnings found (may be good or warnings suppressed)"

echo ""
echo "📊 Method 2: Manual complexity estimation..."
echo ""

# Count decision points in main source files
for file in src/*.cs; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")
        echo "📄 $filename:"
        
        # Count if/else, while, for, foreach, switch, case, catch, &&, ||, ?:
        ifs=$(grep -c -E '\bif\s*\(' "$file" 2>/dev/null || echo "0")
        elses=$(grep -c -E '\belse\b' "$file" 2>/dev/null || echo "0")
        whiles=$(grep -c -E '\bwhile\s*\(' "$file" 2>/dev/null || echo "0")
        fors=$(grep -c -E '\bfor\s*\(' "$file" 2>/dev/null || echo "0")
        foreach=$(grep -c -E '\bforeach\s*\(' "$file" 2>/dev/null || echo "0")
        switches=$(grep -c -E '\bswitch\s*\(' "$file" 2>/dev/null || echo "0")
        cases=$(grep -c -E '\bcase\s+' "$file" 2>/dev/null || echo "0")
        catches=$(grep -c -E '\bcatch\s*[\(\{]' "$file" 2>/dev/null || echo "0")
        ands=$(grep -c -E '&&' "$file" 2>/dev/null || echo "0")
        ors=$(grep -c -E '\|\|' "$file" 2>/dev/null || echo "0")
        ternary=$(grep -c -E '\?.*:' "$file" 2>/dev/null || echo "0")
        
        # Basic complexity calculation (each decision point adds 1)
        # Convert to integers properly
        ifs=${ifs:-0}
        elses=${elses:-0}
        whiles=${whiles:-0}
        fors=${fors:-0}
        foreach=${foreach:-0}
        switches=${switches:-0}
        cases=${cases:-0}
        catches=${catches:-0}
        ands=${ands:-0}
        ors=${ors:-0}
        ternary=${ternary:-0}
        
        complexity=$((1 + ifs + elses + whiles + fors + foreach + switches + cases + catches + ands + ors + ternary))
        
        echo "   Decision points:"
        echo "   - if statements: $ifs"
        echo "   - else statements: $elses" 
        echo "   - while loops: $whiles"
        echo "   - for loops: $fors"
        echo "   - foreach loops: $foreach"
        echo "   - switch statements: $switches"
        echo "   - case statements: $cases"
        echo "   - catch blocks: $catches"
        echo "   - && operators: $ands"
        echo "   - || operators: $ors"
        echo "   - ternary operators: $ternary"
        echo "   📈 Estimated complexity: $complexity"
        
        # Complexity assessment
        if [ $complexity -le 10 ]; then
            echo "   ✅ Low complexity (≤10)"
        elif [ $complexity -le 20 ]; then
            echo "   ⚠️  Moderate complexity (11-20)"
        elif [ $complexity -le 50 ]; then
            echo "   🔴 High complexity (21-50)"
        else
            echo "   💀 Very high complexity (>50)"
        fi
        echo ""
    fi
done

echo "📊 Method 3: Using dotnet-counters (if available)..."
echo ""

# Check if dotnet-counters is available
if command -v dotnet-counters &> /dev/null; then
    echo "dotnet-counters found - can be used for runtime complexity monitoring"
else
    echo "dotnet-counters not installed. Install with:"
    echo "dotnet tool install --global dotnet-counters"
fi

echo ""
echo "📊 Method 4: Integration with coverage analysis..."
echo ""

# Run coverage and analyze the complexity of covered vs uncovered code
if [ -f "coverage.sh" ]; then
    echo "Running coverage analysis to identify complex uncovered areas..."
    ./coverage.sh | tail -10
else
    echo "coverage.sh not found"
fi

echo ""
echo "💡 RECOMMENDATIONS:"
echo "==================="
echo "• Keep method complexity ≤ 10 for maintainability"
echo "• Refactor methods with complexity > 20"
echo "• Focus testing on high-complexity methods first"
echo "• Consider extracting complex conditions into separate methods"
echo "• Use early returns to reduce nesting and complexity"
