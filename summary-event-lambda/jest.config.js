module.exports = {
  testEnvironment: 'node',
  reporters: [
    'default',
    [ 'jest-junit', { outputDirectory: './test-results', outputName: 'js-junit.xml' } ]
  ],
  testResultsProcessor: 'jest-junit'
};
