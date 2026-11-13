# Spec Delta

## ADDED Requirements

### Requirement: Stub Controller Replay Source
The system SHALL use registered stub controllers as an additional response source in Replay mode, dispatching matching requests to MVC-routed controllers before evaluating programmatic rules.

#### Scenario: Stub controller serves recorded response in Replay mode
- **WHEN** a stub controller is registered for a contract endpoint and a matching request arrives in Replay mode
- **THEN** the system dispatches the request to the stub controller and returns the controller's response without evaluating programmatic rules.

#### Scenario: Fall through to programmatic rules when no stub matches
- **WHEN** a request does not match any registered stub controller route but matches a programmatic rule
- **THEN** the system evaluates the programmatic rule and returns its configured response.

#### Scenario: Stub controller extensibility hooks override recorded data
- **WHEN** a stub controller's partial-class hook method returns a non-null value for a request
- **THEN** the hook's return value is used as the response instead of the controller's default recorded data.
