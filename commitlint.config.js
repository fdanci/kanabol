// Conventional Commits with the scopes from docs/SPEC.md section 7.5.
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'scope-enum': [
      2,
      'always',
      ['api', 'web', 'db', 'auth', 'board', 'tasks', 'infra', 'ci', 'docs'],
    ],
  },
};
