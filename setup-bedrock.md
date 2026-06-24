# Bedrock setup for the Claude Code Review workflow

This documents the AWS prerequisites for `.github/workflows/claude-code-review.yml`,
which runs `anthropics/claude-code-action@v1` against **Amazon Bedrock** instead of
the Anthropic API.

- **Default model** (`ANTHROPIC_MODEL`): Claude Sonnet 4.6 — `us.anthropic.claude-sonnet-4-6`
- **Small/fast model** (`ANTHROPIC_SMALL_FAST_MODEL`): Claude Haiku 4.5 — `us.anthropic.claude-haiku-4-5-20251001-v1:0`

> **Note:** the Haiku inference-profile ID requires the `-v1:0` version suffix.
> Without it, Bedrock returns `ValidationException: The provided model identifier
> is invalid`, and because the SDK issues a small/fast-model call at the start of
> the run, the *entire* review fails instantly (no comments posted) even though
> the main Sonnet model is fine.

Authentication is **GitHub OIDC → AWS IAM role** (no long-lived AWS keys, no
`CLAUDE_CODE_OAUTH_TOKEN`).

---

## Values to fill in

Replace these everywhere they appear below and in the workflow:

| Placeholder        | Meaning                                   | Example                                  |
| ------------------ | ----------------------------------------- | ---------------------------------------- |
| `<ACCOUNT_ID>`     | Your 12-digit AWS account ID              | `123456789012`                           |
| `<OWNER>/<REPO>`   | This GitHub repo slug                     | `my-org/claude-review-poc`               |
| `<REGION>`         | Bedrock region (used in the workflow)     | `us-east-1`                              |
| `<ROLE_NAME>`      | Name for the IAM role you create          | `github-bedrock-review`                  |

> **Region & model IDs:** the `us.` inference-profile prefix is for US regions.
> In an EU/APAC region use `eu.` / `apac.` and confirm the exact profile IDs in
> Bedrock console → *Cross-region inference*.

---

## Step 1 — Enable Bedrock model access

Bedrock console → **Model access** → request/enable access for **both**:

- Anthropic — Claude Sonnet 4.6
- Anthropic — Claude Haiku 4.5

Access is per-region; enable it in `<REGION>`.

---

## Step 2 — Add the GitHub OIDC provider (once per account)

Skip if you already have a `token.actions.githubusercontent.com` provider.

**Console:** IAM → *Identity providers* → *Add provider* → OpenID Connect
- Provider URL: `https://token.actions.githubusercontent.com`
- Audience: `sts.amazonaws.com`

**CLI:**

```bash
aws iam create-open-id-connect-provider \
  --url https://token.actions.githubusercontent.com \
  --client-id-list sts.amazonaws.com
```

---

## Step 3 — Create the IAM role with a trust policy

Save as `trust-policy.json` (scopes the role to this repo only):

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": { "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com" },
    "Action": "sts:AssumeRoleWithWebIdentity",
    "Condition": {
      "StringEquals": { "token.actions.githubusercontent.com:aud": "sts.amazonaws.com" },
      "StringLike":   { "token.actions.githubusercontent.com:sub": "repo:<OWNER>/<REPO>:*" }
    }
  }]
}
```

> To restrict further to PR events only, replace the `sub` with
> `repo:<OWNER>/<REPO>:pull_request`.

```bash
aws iam create-role \
  --role-name <ROLE_NAME> \
  --assume-role-policy-document file://trust-policy.json
```

---

## Step 4 — Attach the Bedrock permission policy

Save as `bedrock-policy.json`. Lists both models' inference-profile **and**
foundation-model ARNs (cross-region profiles route to the underlying
foundation models, so both are required):

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Action": ["bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream"],
    "Resource": [
      "arn:aws:bedrock:*:<ACCOUNT_ID>:inference-profile/us.anthropic.claude-sonnet-4-6",
      "arn:aws:bedrock:*::foundation-model/anthropic.claude-sonnet-4-6",
      "arn:aws:bedrock:*:<ACCOUNT_ID>:inference-profile/us.anthropic.claude-haiku-4-5-20251001-v1:0",
      "arn:aws:bedrock:*::foundation-model/anthropic.claude-haiku-4-5-20251001-v1:0"
    ]
  }]
}
```

```bash
aws iam put-role-policy \
  --role-name <ROLE_NAME> \
  --policy-name bedrock-invoke \
  --policy-document file://bedrock-policy.json
```

---

## Step 5 — Wire the role into the workflow

In `.github/workflows/claude-code-review.yml`, set `role-to-assume` and `aws-region`:

```yaml
      - name: Configure AWS credentials (OIDC)
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::<ACCOUNT_ID>:role/<ROLE_NAME>
          aws-region: <REGION>
```

The job already grants the required permission:

```yaml
    permissions:
      id-token: write   # OIDC token for AWS
```

---

## Step 6 — Test

Open or sync a pull request. The `Claude Code Review` job should assume the role,
call Bedrock, and post a review. Watch the Actions log for the
`Configure AWS credentials` and `Run Claude Code Review` steps.

---

## Cleanup

`secrets.CLAUDE_CODE_OAUTH_TOKEN` is no longer referenced — delete it from the
repo's *Settings → Secrets and variables → Actions* once the Bedrock path works.

---

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `Not authorized to perform sts:AssumeRoleWithWebIdentity` | Trust-policy `sub`/`aud` doesn't match, or OIDC provider missing. Check `<OWNER>/<REPO>` and that `id-token: write` is set. |
| `AccessDeniedException` calling Bedrock | Permission policy missing an ARN, or model access not enabled. Ensure **both** Sonnet and Haiku ARNs are present (a missing Haiku ARN often fails on a *background* call, not the main review). |
| `ValidationException` / model id not found | The inference-profile ID isn't available in `<REGION>`, or the region prefix is wrong (`us.` vs `eu.`/`apac.`). Confirm in Bedrock console → Cross-region inference. |
| Model-access "pending" | Some Anthropic models require access approval; wait for it to flip to *Access granted*. |
