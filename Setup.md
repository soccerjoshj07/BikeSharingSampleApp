# Commands

URL: https://docs.microsoft.com/en-us/azure/dev-spaces/quickstart-team-development

```bash
# create aks cluster
az group create --name az-dev-spaces-rg --location eastus
az aks create -g az-dev-spaces-rg -n devspacesaks --location eastus --disable-rbac --generate-ssh-keys

# enable dev spaces on aks cluster
az aks use-dev-spaces -g az-dev-spaces-rg -n MyAKS --space dev --yes

# get sample code
git clone https://github.com/Azure/dev-spaces
cd dev-spaces/samples/BikeSharingApp/

# retrieve hostsuffix for dev
azds show-context

# update charts/values.yaml with the HostSuffix

# run the sample app in k8s
cd charts/
helm install bikesharing . --dependency-update --namespace dev --atomic

# show the uris and launch the bikesharingweb app 
azds list-uris

# create child dev spaces
azds space select -n dev/azureuser1 -y
azds space select -n dev/azureuser2 -y

# switch to other dev spaces
azds space select -n azureuser2

# list the uris for selected dev space
azds list-uris

# update the code (BikeSharingWeb/components/Header.js)

# build and run service in dev space
cd ../BikeSharingWeb
azds up

# list all urs for all dev spaces
azds list-uris --all

# clean up your azure resources
az group delete --name az-dev-spaces-rg --yes --no-wait

```
