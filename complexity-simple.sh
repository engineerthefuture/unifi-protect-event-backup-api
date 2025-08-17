#!/bin/bash

# Simple Cyclomatic Complexity Analysis
echo "🔍 CYCLOMATIC COMPLEXITY ANALYSIS 🔍"
echo "====================================="

cd "$(dirname "$0")"

echo "📊 Analyzing complexity in source files..."
echo ""

for file in src/*.cs; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")
        echo "📄 $filename:"
        
        # Count decision points more reliably
        ifs=$(grep -o '\bif\s*(' "$file" | wc -l | tr -d ' ')
        elses=$(grep -o '\belse\b' "$file" | wc -l | tr -d ' ')
        whiles=$(grep -o '\bwhile\s*(' "$file" | wc -l | tr -d ' ')
        fors=$(grep -o '\bfor\s*(' "$file" | wc -l | tr -d ' ')
        foreach=$(grep -o '\bforeach\s*(' "$file" | wc -l | tr -d ' ')
        switches=$(grep -o '\bswitch\s*(' "$file" | wc -l | tr -d ' ')
        cases=$(grep -o '\bcase\s' "$file" | wc -l | tr -d ' ')
        catches=$(grep -o '\bcatch\s*(' "$file" | wc -l | tr -d ' ')
        ands=$(grep -o '&&' "$file" | wc -l | tr -d ' ')
        ors=$(grep -o '||' "$file" | wc -l | tr -d ' ')
        
        # Calculate total complexity
        total=$((ifs + elses + whiles + fors + foreach + switches + cases + catches + ands + ors + 1))
        
        echo "   Decision points:"
        echo "   - if: $ifs, else: $elses, while: $whiles, for: $fors"
        echo "   - foreach: $foreach, switch: $switches, case: $cases, catch: $catches"
        echo "   - &&: $ands, ||: $ors"
        echo "   📈 Total complexity: $total"
        
        if [ $total -le 10 ]; then
            echo "   ✅ Low complexity"
        elif [ $total -le 20 ]; then
            echo "   ⚠️  Moderate complexity"
        else
            echo "   🔴 High complexity - consider refactoring"
        fi
        echo ""
    fi
done

echo "💡 Use these tools for detailed analysis:"
echo "• SonarQube/SonarCloud for comprehensive metrics"
echo "• Visual Studio Code with SonarLint extension" 
echo "• dotnet-counters for runtime analysis"
echo "• Code Metrics PowerTool for Visual Studio"
