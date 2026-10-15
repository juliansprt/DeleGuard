# ADR-006 - Bind permits to resource/action context

Status: Accepted.

High-risk permits include resource identity and relevant snapshot/hash to reduce TOCTOU. If material attributes change after authorization, require reauthorization.
