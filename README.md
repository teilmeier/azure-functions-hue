# Azure Functions Controller for Philips Hue

Control Philips Hue system using Azure Functions.

This implementation is based on the https://github.com/michielpost/Q42.HueApi. It includes the following components

- Authentication and authorization based on https://developers.meethue.com/develop/hue-api/remote-authentication-oauth/
- Access token management using Durable Entities
- Sample Function to execute a Hue command (enable/disable motion sensors)
- GitHub Action for Build and Deployment to Azure Functions

## Getting started

### 1. Remote Hue API setup

* Set-up an Remote Hue API app at https://developers.meethue.com/my-apps/
* The callback URL will be in the format 'https://{YOUR-FUNCTION-NAME}.azurewebsites.net/api/RedeemCode'. You can change this value afterwards if you do not have a Function App yet.
* Remember the values of 'AppId', 'ClientId', 'ClientSecret'. You'll add them to your FUnction App's settings later.

### 2. Azure Function provisioning

* Create an Azure Function App using the Azure Portal, Visual Studio or any tool of your choice. Make sure it runs on the .NET 6 runtime stack.
* Go to the Hue Developer Portal. Compose the 'Callback URL' based on the Function App name you chose and configure it in your Remote Hue API app.
* In the Azue Portal open Configuration section of your Function App and add the following application settings:

| Name               | Value                                                     |
|--------------------|-----------------------------------------------------------|
| HueAppId           | AppId retrieved from Remote Hue API setup                 |
| HueClientId        | ClientId retrieved from Remote Hue API setup              |
| HueClientSecret    | ClientSecret retrieved from Remote Hue API setup          |
| HueInstanceId      | Used to switch instance ID of Durable Entities (optional) |

### 3. Set-up GitHub Actions workflow for build and deployment

* Fork this repo and continiue working on your personal GitHub account's repository.
* Download the publishing profile of your Function App. In the Azure Portal you will find it at 'Overview' or 'Deployment Center' --> 'Get/Manage publish profile'.
* Create two new secrets in your forked repository at Settings --> Secrets --> Actions.

| Name                                | Value                                              |
|-------------------------------------|----------------------------------------------------|
| AZURE_FUNCTIONAPP_NAME              | Name of your provisioned Function App              |
| AZURE_FUNCTIONAPP_PUBLISH_PROFILE   | Content of the downloaded publishing profile file  |

* Go to the 'Actions' tab to enable and run the GitHub Actions workflow. I configured a manual trigger and a trigger that starts a new deployment on every commit to the main branch.

### 3. Authentication

* After the successful release process you will see multiple Functions in your Function App. 

| Name                      | Description                                                |
|---------------------------|------------------------------------------------------------|
| HueState                  | Durable Entity used to store authentication data           |
| InitializeAuthentication  | Implements the Hue Remote Authentication OAuth2.0 flow     |
| RedeemCode                | Provides callback URL endpoint for OAuth2.0 auth code flow |
| ResetAuthentication       | Clear all authentication data from durable entity          |
| SetSensorState            | Sample function (acts as a template for your own logic!)   |

* To initialize the authentication call 'InitializeAuthentication' or 'SetSensorState' in a web browser. It will guide you through the OAuth2.0 flow.
* You will see 'OAuth authorization flow completed.' in your browser when the setup succeeded.

## Kudos
Thanks to Michiel Post for the quick response on my feedback and approving and merging my changes.
