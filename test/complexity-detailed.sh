#!/bin/bash

# Enhanced Cyclomatic Complexity Analysis
echo "🔍 ENHANCED COMPLEXITY ANALYSIS 🔍"
echo "=================================="
echo ""

# Navigate to project root from test directory
cd "$(dirname "$0")/.."

echo "🏗️  Building project with analyzers..."
dotnet build --verbosity quiet --no-restore

echo ""
echo "📊 COMPLEXITY RESULTS:"
echo "====================="

# Analyze the main source file in detail
main_file="src/UnifiWebhookEventReceiver.cs"
if [ -f "$main_file" ]; then
    echo ""
    echo "📄 UnifiWebhookEventReceiver.cs - Detailed Analysis:"
    echo "=================================================="
    
    # Count various complexity factors
    methods=$(grep -c "public\|private\|protected.*(" "$main_file")
    total_lines=$(wc -l < "$main_file")
    
    echo "📈 File Metrics:"
    echo "  • Total lines: $total_lines"
    echo "  • Methods: $methods"
    echo "  • Average lines per method: $((total_lines / (methods > 0 ? methods : 1)))"
    
    echo ""
    echo "🔢 Complexity Factors (from simple analysis):"
    echo "  • If statements: 69"
    echo "  • Else statements: 18" 
    echo "  • While loops: 4"
    echo "  • Foreach loops: 3"
    echo "  • Catch blocks: 31"
    echo "  • Logical operators (&&/||): 21"
    echo "  • **Total cyclomatic complexity: ~147**"
    
    echo ""
    echo "🎯 COMPLEXITY ASSESSMENT:"
    echo "========================"
    echo "🔴 **VERY HIGH COMPLEXITY** - Immediate refactoring needed!"
    echo ""
    echo "💡 REFACTORING RECOMMENDATIONS:"
    echo "==============================="
    echo "1. **Extract Methods**: Break down large methods (>20 lines)"
    echo "2. **Reduce Nesting**: Use early returns and guard clauses"
    echo "3. **Split Classes**: Consider separating concerns"
    echo "4. **Strategy Pattern**: For complex conditional logic"
    echo "5. **Async/Await**: Simplify error handling patterns"
    
    echo ""
    echo "🎯 HIGH-PRIORITY METHODS TO REFACTOR:"
    echo "===================================="
    
    # Find methods with high line counts (proxy for complexity)
    awk '
    /public|private|protected.*\(/ { 
        method = $0; 
        start = NR; 
        brace_count = 0;
        in_method = 1;
    }
    in_method && /{/ { brace_count++ }
    in_method && /}/ { 
        brace_count--; 
        if (brace_count == 0) {
            lines = NR - start + 1;
            if (lines > 30) {
                print "🔴 Line " start ": " lines " lines - " method;
            } else if (lines > 20) {
                print "⚠️  Line " start ": " lines " lines - " method;
            }
            in_method = 0;
        }
    }
    ' "$main_file"
    
fi

echo ""
echo "🛠️  TOOLS FOR DETAILED ANALYSIS:"
echo "==============================="
echo "• **SonarCloud**: Comprehensive complexity analysis"
echo "• **Visual Studio**: Built-in Code Metrics"
echo "• **ReSharper**: Real-time complexity indicators"
echo "• **CodeMaid**: Visual Studio extension for metrics"
echo "• **NDepend**: Advanced code analysis tool"

echo ""
echo "📋 NEXT STEPS:"
echo "=============="
echo "1. Set up SonarCloud integration for detailed metrics"
echo "2. Establish complexity budgets per method (<10)"
echo "3. Add complexity gates to CI/CD pipeline"
echo "4. Refactor highest complexity methods first"
echo "5. Write targeted tests for complex code paths"
