#!/bin/bash

# Simple code coverage script using coverlet and XPlat
echo "🧪 Running tests with code coverage..."

# Navigate to test directory (script is already in test folder)
cd "$(dirname "$0")"

# Clean previous results
rm -rf TestResults
rm -rf ../coverage-results

# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Find the coverage file
COVERAGE_FILE=$(find TestResults -name "coverage.cobertura.xml" | head -1)

if [ -f "$COVERAGE_FILE" ]; then
    echo ""
    echo "📊 CODE COVERAGE RESULTS"
    echo "========================"
    
    # Extract metrics using grep and sed
    METRICS=$(grep '<coverage' "$COVERAGE_FILE" | head -1)
    
    LINE_RATE=$(echo "$METRICS" | sed 's/.*line-rate="\([^"]*\)".*/\1/')
    BRANCH_RATE=$(echo "$METRICS" | sed 's/.*branch-rate="\([^"]*\)".*/\1/')
    LINES_COVERED=$(echo "$METRICS" | sed 's/.*lines-covered="\([^"]*\)".*/\1/')
    LINES_VALID=$(echo "$METRICS" | sed 's/.*lines-valid="\([^"]*\)".*/\1/')
    BRANCHES_COVERED=$(echo "$METRICS" | sed 's/.*branches-covered="\([^"]*\)".*/\1/')
    BRANCHES_VALID=$(echo "$METRICS" | sed 's/.*branches-valid="\([^"]*\)".*/\1/')
    
    # Calculate percentages
    LINE_PERCENT=$(echo "$LINE_RATE * 100" | bc -l | xargs printf "%.2f")
    BRANCH_PERCENT=$(echo "$BRANCH_RATE * 100" | bc -l | xargs printf "%.2f")
    
    echo "📈 Line Coverage:   ${LINE_PERCENT}% (${LINES_COVERED}/${LINES_VALID} lines)"
    echo "🌿 Branch Coverage: ${BRANCH_PERCENT}% (${BRANCHES_COVERED}/${BRANCHES_VALID} branches)"
    echo ""
    echo "Coverage file: test/$COVERAGE_FILE"
    
    # Simple threshold check
    THRESHOLD=80
    LINE_CHECK=$(echo "$LINE_PERCENT >= $THRESHOLD" | bc -l)
    BRANCH_CHECK=$(echo "$BRANCH_PERCENT >= $THRESHOLD" | bc -l)
    
    if [ "$LINE_CHECK" = "1" ] && [ "$BRANCH_CHECK" = "1" ]; then
        echo "✅ Coverage meets 80% threshold!"
    else
        echo "❌ Coverage below 80% threshold"
        echo "   Need: ${THRESHOLD}% line and branch coverage"
    fi
else
    echo "❌ Coverage file not found!"
fi
